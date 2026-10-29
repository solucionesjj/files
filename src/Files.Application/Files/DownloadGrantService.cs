using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Files;

/// <summary>Response of a successful grant issue (OpenAPI <c>DownloadGrant</c>).</summary>
public sealed record DownloadGrantIssued(string GrantToken, DateTimeOffset ExpiresAt, string DownloadUrl);

/// <summary>A successfully redeemed grant plus the file it authorizes to stream.</summary>
public sealed record GrantRedeemed(Guid GrantId, FileEntity File);

/// <summary>Owner download grants (ADR-0005): 256-bit, single-use, 120 s, counted on completion.</summary>
public interface IDownloadGrantService
{
    Task<ServiceResult<DownloadGrantIssued>> IssueOwnerAsync(
        Guid fileId,
        Guid ownerUserId,
        string? ipHash,
        CancellationToken ct = default);

    /// <summary>Atomically consumes an issued grant (issued → redeemed) and re-checks availability.</summary>
    Task<ServiceResult<GrantRedeemed>> RedeemAsync(string grantToken, CancellationToken ct = default);

    /// <summary>Marks a streamed grant completed and increments the file download counter once.</summary>
    Task CompleteAsync(Guid grantId, Guid fileId, long bytesSent, CancellationToken ct = default);

    /// <summary>Marks a grant failed (unavailable file, missing object, or aborted stream).</summary>
    Task FailAsync(Guid grantId, Guid fileId, string reasonCode, CancellationToken ct = default);
}

public sealed partial class DownloadGrantService(
    IFileRepository files,
    IDownloadGrantRepository grants,
    IDownloadLinkRepository links,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    StorageOptions storage,
    ILogger<DownloadGrantService> logger) : IDownloadGrantService
{
    public async Task<ServiceResult<DownloadGrantIssued>> IssueOwnerAsync(
        Guid fileId,
        Guid ownerUserId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var file = await files.GetOwnedFileAsync(fileId, ownerUserId, ct);
        if (file is null)
        {
            return ServiceResult<DownloadGrantIssued>.Fail(404, "file_not_found", "File not found");
        }

        if (!IsDownloadable(file, timeProvider.GetUtcNow()))
        {
            return ServiceResult<DownloadGrantIssued>.Fail(404, "file_unavailable", "File is not available for download");
        }

        var token = GrantTokens.Generate();
        var grant = new DownloadGrant
        {
            FileId = file.Id,
            GrantTokenHash = GrantTokens.Hash(token),
            Source = GrantSource.Owner,
            IssuedToUserId = ownerUserId,
            IssuedIpHash = ipHash,
            State = GrantState.Issued,
            ExpiresAt = timeProvider.GetUtcNow().AddSeconds(storage.GrantTtlSeconds),
        };

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            grants.Add(grant);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        Log.GrantIssued(logger, grant.Id);
        return ServiceResult<DownloadGrantIssued>.Ok(new DownloadGrantIssued(
            token, grant.ExpiresAt, $"/v1/download-grants/{token}"));
    }

    public async Task<ServiceResult<GrantRedeemed>> RedeemAsync(string grantToken, CancellationToken ct = default)
    {
        if (!GrantTokens.TryDecode(grantToken, out var tokenHash))
        {
            return ServiceResult<GrantRedeemed>.Fail(404, "grant_not_found", "Grant not found");
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        GrantClaim? claim;
        try
        {
            claim = await grants.ClaimAsync(tokenHash, now, ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        if (claim is null)
        {
            await unitOfWork.RollbackAsync(ct);
            return ClassifyNonClaim(await grants.GetStateAsync(tokenHash, ct));
        }

        try
        {
            // A revoked/expired link invalidates its grants immediately (FR-LINK-006/013): re-check
            // the link before streaming, after the atomic claim.
            if (claim.LinkId is { } linkId && !await links.IsActiveAsync(linkId, now, ct))
            {
                await grants.FailAsync(claim.GrantId, claim.FileId, "link_revoked", ct);
                await unitOfWork.CommitAsync(ct);
                return ServiceResult<GrantRedeemed>.Fail(404, "grant_not_found", "Grant not found");
            }

            var file = await files.GetByIdTrackedAsync(claim.FileId, ct);
            if (file is null || !IsDownloadable(file, now))
            {
                await grants.FailAsync(claim.GrantId, claim.FileId, "file_unavailable", ct);
                await unitOfWork.CommitAsync(ct);
                return ServiceResult<GrantRedeemed>.Fail(404, "file_unavailable", "File is not available for download");
            }

            await unitOfWork.CommitAsync(ct);
            return ServiceResult<GrantRedeemed>.Ok(new GrantRedeemed(claim.GrantId, file));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    public async Task CompleteAsync(Guid grantId, Guid fileId, long bytesSent, CancellationToken ct = default)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await grants.CompleteAsync(grantId, fileId, bytesSent, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    public async Task FailAsync(Guid grantId, Guid fileId, string reasonCode, CancellationToken ct = default)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await grants.FailAsync(grantId, fileId, reasonCode, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static ServiceResult<GrantRedeemed> ClassifyNonClaim(GrantState? state) =>
        state switch
        {
            null => ServiceResult<GrantRedeemed>.Fail(404, "grant_not_found", "Grant not found"),
            GrantState.Issued or GrantState.Expired =>
                ServiceResult<GrantRedeemed>.Fail(410, "grant_expired", "Grant expired"),
            GrantState.Failed =>
                ServiceResult<GrantRedeemed>.Fail(404, "grant_not_found", "Grant not found"),
            _ => ServiceResult<GrantRedeemed>.Fail(409, "grant_consumed", "Grant already used"),
        };

    private static bool IsDownloadable(FileEntity file, DateTimeOffset now) =>
        file.Status == FileStatus.Available
        && (file.ExpiresAt is null || file.ExpiresAt > now)
        && file.ScanStatus is ScanStatus.Clean or ScanStatus.NotScanned;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Download grant {GrantId} issued")]
        public static partial void GrantIssued(ILogger logger, Guid grantId);
    }
}
