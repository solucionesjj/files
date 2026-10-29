using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Files;

/// <summary>Result of a successful link creation (OpenAPI <c>LinkCreated</c>).</summary>
public sealed record LinkCreatedResult(DownloadLink Link, string Url, string Token, string DownloadKey);

/// <summary>A page of links (OpenAPI <c>LinkList</c>).</summary>
public sealed record LinkListResult(IReadOnlyList<DownloadLink> Items, LinkCursor? NextCursor, bool HasMore);

/// <summary>Per-link download statistics (OpenAPI <c>LinkStats</c>).</summary>
public sealed record LinkStatsResult(
    Guid LinkId,
    int SuccessfulDownloads,
    int ReservedDownloads,
    int FailedAttempts,
    DateTimeOffset? LastDownloadAt,
    IReadOnlyList<DownloadEventRecord> RecentEvents);

/// <summary>Non-sensitive public landing metadata (OpenAPI <c>LinkPublicInfo</c>).</summary>
public sealed record LinkPublicInfoResult(
    string? FileName,
    long? SizeBytes,
    string? ContentType,
    bool RequiresKey,
    bool RequiresAuthentication,
    DateTimeOffset? ExpiresAt,
    int? RemainingDownloads,
    string? OwnerDisplayName);

/// <summary>Public-link lifecycle: create/list/revoke/stats (owner-scoped) and anonymous metadata/redemption.</summary>
public interface IPublicLinkService
{
    Task<ServiceResult<LinkCreatedResult>> CreateAsync(
        Guid fileId,
        Guid ownerUserId,
        int? maxDownloads,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<LinkListResult>> ListFileLinksAsync(
        Guid fileId,
        Guid ownerUserId,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default);

    Task<ServiceResult<LinkListResult>> ListMyLinksAsync(
        Guid ownerUserId,
        LinkStatus? status,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default);

    Task<ServiceResult<bool>> RevokeAsync(
        Guid linkId,
        Guid ownerUserId,
        string? reason,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<LinkStatsResult>> GetStatsAsync(Guid linkId, Guid ownerUserId, CancellationToken ct = default);

    Task<ServiceResult<LinkPublicInfoResult>> GetPublicAsync(string token, CancellationToken ct = default);

    Task<ServiceResult<DownloadGrantIssued>> RedeemAsync(
        string token,
        string key,
        string? ipHash,
        CancellationToken ct = default);
}

public sealed partial class PublicLinkService(
    IFileRepository files,
    IUserRepository users,
    IDownloadLinkRepository links,
    IDownloadGrantRepository grants,
    IKeyAttemptRepository keyAttempts,
    IPlatformSettingsRepository settings,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    LinksOptions linkOptions,
    KeyThrottle keyThrottle,
    StorageOptions storage,
    ILogger<PublicLinkService> logger) : IPublicLinkService
{
    private const string PublicLinksEnabledKey = "public_links_enabled";
    private const string PublicLinkTtlDaysKey = "public_link_ttl_days";
    private const string MaxLinksPerFileKey = "max_links_per_file";
    private const string MaxLinksPerUserKey = "max_links_per_user";

    private const int DefaultMaxLinksPerFile = 100;
    private const int DefaultMaxLinksPerUser = 1000;

    public async Task<ServiceResult<LinkCreatedResult>> CreateAsync(
        Guid fileId,
        Guid ownerUserId,
        int? maxDownloads,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        if (await settings.GetBoolAsync(PublicLinksEnabledKey, ct) is false)
        {
            return ServiceResult<LinkCreatedResult>.Fail(409, "public_links_disabled", "Public links are disabled");
        }

        var file = await files.GetOwnedFileAsync(fileId, ownerUserId, ct);
        if (file is null)
        {
            return ServiceResult<LinkCreatedResult>.Fail(404, "file_not_found", "File not found");
        }

        if (file.AccessMode == AccessMode.IntegrationOnly)
        {
            return ServiceResult<LinkCreatedResult>.Fail(409, "file_unavailable", "Integration files cannot be shared");
        }

        var now = timeProvider.GetUtcNow();
        if (!IsDownloadable(file, now))
        {
            return ServiceResult<LinkCreatedResult>.Fail(409, "file_unavailable", "File is not available for sharing");
        }

        var maxPerFile = await settings.GetIntAsync(MaxLinksPerFileKey, ct) ?? DefaultMaxLinksPerFile;
        var maxPerUser = await settings.GetIntAsync(MaxLinksPerUserKey, ct) ?? DefaultMaxLinksPerUser;

        if (await links.CountActiveForFileAsync(fileId, now, ct) >= maxPerFile)
        {
            return ServiceResult<LinkCreatedResult>.Fail(409, "link_limit_reached", "Too many active links for this file");
        }

        if (await links.CountActiveByCreatorAsync(ownerUserId, now, ct) >= maxPerUser)
        {
            return ServiceResult<LinkCreatedResult>.Fail(409, "link_limit_reached", "Too many active links");
        }

        var ttlDays = await settings.GetIntAsync(PublicLinkTtlDaysKey, ct) ?? linkOptions.PublicTtlDays;
        if (ttlDays <= 0)
        {
            ttlDays = linkOptions.PublicTtlDays;
        }

        var token = LinkTokens.Generate();
        var key = DownloadKeys.Generate();
        var keyHash = DownloadKeys.Hash(linkOptions.LinkKeyPepper, key, DownloadKeys.GenerateSalt());

        var link = new DownloadLink
        {
            FileId = file.Id,
            TokenHash = LinkTokens.Hash(token),
            AccessMode = LinkAccessMode.Public,
            AllowAllTenant = false,
            DownloadKeyHash = keyHash,
            DownloadKeyVersion = (short)linkOptions.LinkKeyPepperVersion,
            Status = LinkStatus.Active,
            ExpiresAt = now.AddDays(ttlDays),
            MaxDownloads = maxDownloads,
            CreatedByType = LinkCreatorType.User,
            CreatedBy = ownerUserId,
        };

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            links.Add(link);
            await WriteFileAuditAsync(
                file.Id, ActorType.User, ownerUserId, "link.created",
                JsonSerializer.Serialize(new { linkId = link.Id, accessMode = "public", expiresAt = link.ExpiresAt }),
                reason: null, traceId, ipHash, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        Log.LinkCreated(logger, link.Id, file.Id);
        return ServiceResult<LinkCreatedResult>.Ok(new LinkCreatedResult(link, $"/d/{token}", token, key));
    }

    public async Task<ServiceResult<LinkListResult>> ListFileLinksAsync(
        Guid fileId,
        Guid ownerUserId,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        if (await files.GetOwnedFileAsync(fileId, ownerUserId, ct) is null)
        {
            return ServiceResult<LinkListResult>.Fail(404, "file_not_found", "File not found");
        }

        var rows = await links.ListForFileAsync(fileId, cursor, limit + 1, ct);
        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows.ToList();

        return ServiceResult<LinkListResult>.Ok(new LinkListResult(
            page,
            hasMore && page.Count > 0 ? new LinkCursor(page[^1].CreatedAt, page[^1].Id) : null,
            hasMore));
    }

    public async Task<ServiceResult<LinkListResult>> ListMyLinksAsync(
        Guid ownerUserId,
        LinkStatus? status,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var rows = await links.ListByCreatorAsync(ownerUserId, status, cursor, limit + 1, timeProvider.GetUtcNow(), ct);
        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows.ToList();

        return ServiceResult<LinkListResult>.Ok(new LinkListResult(
            page,
            hasMore && page.Count > 0 ? new LinkCursor(page[^1].CreatedAt, page[^1].Id) : null,
            hasMore));
    }

    public async Task<ServiceResult<bool>> RevokeAsync(
        Guid linkId,
        Guid ownerUserId,
        string? reason,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var link = await links.GetOwnedTrackedAsync(linkId, ownerUserId, ct);
        if (link is null)
        {
            return ServiceResult<bool>.Fail(404, "link_not_found", "Link not found");
        }

        if (link.Status != LinkStatus.Active)
        {
            return ServiceResult<bool>.Ok(true);
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            link.Status = LinkStatus.Revoked;
            link.RevokedAt = now;
            link.RevokedBy = ownerUserId;
            link.RevokedReason = reason;

            await unitOfWork.SaveChangesAsync(ct);
            await links.RevokeGrantsAsync(linkId, ct);
            await WriteFileAuditAsync(
                link.FileId, ActorType.User, ownerUserId, "link.revoked",
                JsonSerializer.Serialize(new { linkId }), reason, traceId, ipHash, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        Log.LinkRevoked(logger, linkId);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<LinkStatsResult>> GetStatsAsync(
        Guid linkId,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var link = await links.GetOwnedAsync(linkId, ownerUserId, ct);
        if (link is null)
        {
            return ServiceResult<LinkStatsResult>.Fail(404, "link_not_found", "Link not found");
        }

        var failedAttempts = await links.CountFailedKeyAttemptsAsync(linkId, ct);
        var lastDownloadAt = await links.GetLastDownloadAtAsync(linkId, ct);
        var recentEvents = await links.ListRecentEventsAsync(linkId, limit: 50, ct);

        return ServiceResult<LinkStatsResult>.Ok(new LinkStatsResult(
            link.Id,
            link.SuccessfulDownloads,
            link.ReservedDownloads,
            failedAttempts,
            lastDownloadAt,
            recentEvents));
    }

    public async Task<ServiceResult<LinkPublicInfoResult>> GetPublicAsync(string token, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        if (!LinkTokens.TryDecode(token, out var tokenHash))
        {
            DownloadKeys.BurnOnDummy(linkOptions.LinkKeyPepper, "00000000");
            return NotFound<LinkPublicInfoResult>();
        }

        var link = await links.GetByTokenHashAsync(tokenHash, ct);
        if (link is null || link.AccessMode != LinkAccessMode.Public)
        {
            DownloadKeys.BurnOnDummy(linkOptions.LinkKeyPepper, "00000000");
            return NotFound<LinkPublicInfoResult>();
        }

        if (link.Status != LinkStatus.Active || (link.ExpiresAt is { } expires && expires <= now))
        {
            return NotFound<LinkPublicInfoResult>();
        }

        var file = await files.GetByIdAsync(link.FileId, ct);
        if (file is null || !IsDownloadable(file, now))
        {
            return NotFound<LinkPublicInfoResult>();
        }

        var ownerName = link.CreatedBy != Guid.Empty
            ? await users.GetByIdAsync(link.CreatedBy, ct)
            : null;

        int? remaining = link.MaxDownloads is { } max
            ? Math.Max(0, max - link.SuccessfulDownloads - link.ReservedDownloads)
            : null;

        return ServiceResult<LinkPublicInfoResult>.Ok(new LinkPublicInfoResult(
            file.OriginalFilename,
            file.SizeBytes,
            file.ContentType,
            RequiresKey: true,
            RequiresAuthentication: false,
            link.ExpiresAt,
            remaining,
            ownerName?.DisplayName));
    }

    public async Task<ServiceResult<DownloadGrantIssued>> RedeemAsync(
        string token,
        string key,
        string? ipHash,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        if (!LinkTokens.TryDecode(token, out var tokenHash))
        {
            DownloadKeys.BurnOnDummy(linkOptions.LinkKeyPepper, key);
            return NotFound<DownloadGrantIssued>();
        }

        var link = await links.GetByTokenHashAsync(tokenHash, ct);
        if (link is null)
        {
            DownloadKeys.BurnOnDummy(linkOptions.LinkKeyPepper, key);
            return NotFound<DownloadGrantIssued>();
        }

        if (link.Status != LinkStatus.Active || (link.ExpiresAt is { } linkExpiry && linkExpiry <= now))
        {
            return NotFound<DownloadGrantIssued>();
        }

        var ip = ipHash ?? "unknown";
        var linkIpKey = $"{link.Id}:{ip}";
        var linkIpLocked = await keyAttempts.GetLockedUntilAsync("link_ip", linkIpKey, ct);
        var linkSoftLocked = await keyAttempts.GetLockedUntilAsync("link", link.Id.ToString(), ct);

        if (KeyThrottle.IsLockedOut(linkIpLocked, linkSoftLocked, now))
        {
            await keyAttempts.AddAsync(link.Id, ip, "locked_out", ct);
            return TooManyAttempts<DownloadGrantIssued>();
        }

        if (!DownloadKeys.Verify(link.DownloadKeyHash!, linkOptions.LinkKeyPepper, key))
        {
            return await HandleFailedKeyAsync(link, ip, linkIpKey, now, ct);
        }

        var file = await files.GetByIdAsync(link.FileId, ct);
        if (file is null || !IsDownloadable(file, now))
        {
            return NotFound<DownloadGrantIssued>();
        }

        return await IssueGrantAsync(link, file, ipHash, now, ct);
    }

    private async Task<ServiceResult<DownloadGrantIssued>> HandleFailedKeyAsync(
        DownloadLink link,
        string ip,
        string linkIpKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await keyAttempts.AddAsync(link.Id, ip, "failed", ct);

        var failuresInWindow = await keyAttempts.CountLinkIpAsync(link.Id, ip, now - keyThrottle.Window, ct);
        var failuresPerHour = await keyAttempts.CountLinkAsync(link.Id, now - KeyThrottle.SoftLockWindow, ct);

        if (keyThrottle.ShouldLockOutLinkIp(failuresInWindow))
        {
            await keyAttempts.UpsertLockoutAsync(
                "link_ip", linkIpKey, now + keyThrottle.LinkIpLockout, "too_many_attempts", ct);
        }

        if (keyThrottle.ShouldSoftLockLink(failuresPerHour))
        {
            await keyAttempts.UpsertLockoutAsync(
                "link", link.Id.ToString(), now + keyThrottle.SoftLock, "soft_lock", ct);
            Log.SoftLockAlert(logger, link.Id);
        }

        var delay = KeyThrottle.DelayForAttempt(failuresInWindow);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }

        return NotFound<DownloadGrantIssued>();
    }

    private async Task<ServiceResult<DownloadGrantIssued>> IssueGrantAsync(
        DownloadLink link,
        FileEntity file,
        string? ipHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var token = GrantTokens.Generate();
        var grant = new DownloadGrant
        {
            FileId = file.Id,
            LinkId = link.Id,
            GrantTokenHash = GrantTokens.Hash(token),
            Source = GrantSource.PublicLink,
            IssuedToUserId = null,
            IssuedIpHash = ipHash,
            State = GrantState.Issued,
            ReservedSlot = true,
            ExpiresAt = now.AddSeconds(storage.GrantTtlSeconds),
        };

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            if (!await links.TryReserveAsync(link.Id, now, ct))
            {
                await unitOfWork.RollbackAsync(ct);
                return NotFound<DownloadGrantIssued>();
            }

            grants.Add(grant);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        Log.GrantIssued(logger, grant.Id, link.Id);
        return ServiceResult<DownloadGrantIssued>.Ok(new DownloadGrantIssued(
            token, grant.ExpiresAt, $"/v1/download-grants/{token}"));
    }

    private async Task WriteFileAuditAsync(
        Guid fileId,
        ActorType actorType,
        Guid? actorId,
        string action,
        string metadata,
        string? reason,
        string? traceId,
        string? ipHash,
        CancellationToken ct)
    {
        await auditWriter.WriteFileAuditAsync(new AuditEvent
        {
            FileId = fileId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            Result = AuditResult.Success,
            Reason = reason,
            Metadata = metadata,
            TraceId = traceId,
            IpHash = ipHash,
        }, ct);
    }

    private static ServiceResult<T> NotFound<T>() =>
        ServiceResult<T>.Fail(404, "link_not_found", "Link not found");

    private static ServiceResult<T> TooManyAttempts<T>() =>
        ServiceResult<T>.Fail(429, "too_many_attempts", "Too many attempts");

    private static bool IsDownloadable(FileEntity file, DateTimeOffset now) =>
        file.Status == FileStatus.Available
        && (file.ExpiresAt is null || file.ExpiresAt > now)
        && file.ScanStatus is ScanStatus.Clean or ScanStatus.NotScanned;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Public link {LinkId} created for file {FileId}")]
        public static partial void LinkCreated(ILogger logger, Guid linkId, Guid fileId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Public link {LinkId} revoked")]
        public static partial void LinkRevoked(ILogger logger, Guid linkId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Public link {LinkId} soft-locked after excessive failed key attempts")]
        public static partial void SoftLockAlert(ILogger logger, Guid linkId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Public-link download grant {GrantId} issued for link {LinkId}")]
        public static partial void GrantIssued(ILogger logger, Guid grantId, Guid linkId);
    }
}
