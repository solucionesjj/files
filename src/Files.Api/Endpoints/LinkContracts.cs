using Files.Application.Files;
using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

public sealed record CreateLinkRequest(string AccessMode, int? MaxDownloads);

public sealed record RedeemPublicLinkRequest(string Key);

public sealed record LinkCreatorResponse(string Type, string? DisplayName);

/// <summary>OpenAPI <c>Link</c> (tokens and keys are never present).</summary>
public sealed record LinkResponse(
    Guid Id,
    Guid FileId,
    string AccessMode,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? MaxDownloads,
    int SuccessfulDownloads,
    int ReservedDownloads,
    DateTimeOffset? RevokedAt,
    LinkCreatorResponse CreatedBy);

/// <summary>OpenAPI <c>LinkCreated</c>: the link plus the one-time share URL and download key.</summary>
public sealed record LinkCreatedResponse(
    Guid Id,
    Guid FileId,
    string AccessMode,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? MaxDownloads,
    int SuccessfulDownloads,
    int ReservedDownloads,
    DateTimeOffset? RevokedAt,
    LinkCreatorResponse CreatedBy,
    string Url,
    string Token,
    string DownloadKey);

public sealed record LinkListResponse(IReadOnlyList<LinkResponse> Items, string? NextCursor, bool HasMore);

public sealed record LinkPublicInfoResponse(
    string? FileName,
    long? SizeBytes,
    string? ContentType,
    bool RequiresKey,
    bool RequiresAuthentication,
    DateTimeOffset? ExpiresAt,
    int? RemainingDownloads,
    string? OwnerDisplayName);

public sealed record DownloadEventResponse(
    Guid Id,
    Guid? FileId,
    Guid? LinkId,
    string ActorType,
    Guid? ActorId,
    string Source,
    string Result,
    string? ReasonCode,
    long BytesSent,
    string? TraceId,
    DateTimeOffset OccurredAt);

public sealed record LinkStatsResponse(
    Guid LinkId,
    int SuccessfulDownloads,
    int ReservedDownloads,
    int FailedAttempts,
    DateTimeOffset? LastDownloadAt,
    IReadOnlyList<DownloadEventResponse> RecentEvents);

public static class LinkMapper
{
    public static LinkResponse ToResponse(DownloadLink link, DateTimeOffset now) => new(
        link.Id,
        link.FileId,
        "public",
        EffectiveStatus(link, now),
        link.CreatedAt,
        link.ExpiresAt,
        link.MaxDownloads,
        link.SuccessfulDownloads,
        link.ReservedDownloads,
        link.RevokedAt,
        new LinkCreatorResponse(CreatorTypeName(link.CreatedByType), null));

    public static LinkCreatedResponse ToCreatedResponse(LinkCreatedResult result, DateTimeOffset now)
    {
        var link = ToResponse(result.Link, now);
        return new LinkCreatedResponse(
            link.Id, link.FileId, link.AccessMode, link.Status, link.CreatedAt, link.ExpiresAt,
            link.MaxDownloads, link.SuccessfulDownloads, link.ReservedDownloads, link.RevokedAt,
            link.CreatedBy, result.Url, result.Token, result.DownloadKey);
    }

    public static LinkPublicInfoResponse ToPublicInfoResponse(LinkPublicInfoResult result) => new(
        result.FileName,
        result.SizeBytes,
        result.ContentType,
        result.RequiresKey,
        result.RequiresAuthentication,
        result.ExpiresAt,
        result.RemainingDownloads,
        result.OwnerDisplayName);

    public static LinkStatsResponse ToStatsResponse(LinkStatsResult result) => new(
        result.LinkId,
        result.SuccessfulDownloads,
        result.ReservedDownloads,
        result.FailedAttempts,
        result.LastDownloadAt,
        result.RecentEvents.Select(e => new DownloadEventResponse(
            e.Id, e.FileId, e.LinkId, e.ActorType, e.ActorId, e.Source, e.Result,
            e.ReasonCode, e.BytesSent, e.TraceId, e.OccurredAt)).ToArray());

    public static string EffectiveStatus(DownloadLink link, DateTimeOffset now) =>
        link.Status == LinkStatus.Active && link.ExpiresAt is { } expires && expires <= now
            ? "expired"
            : StatusName(link.Status);

    private static string StatusName(LinkStatus status) => status switch
    {
        LinkStatus.Active => "active",
        LinkStatus.Revoked => "revoked",
        LinkStatus.Expired => "expired",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string CreatorTypeName(LinkCreatorType type) => type switch
    {
        LinkCreatorType.User => "user",
        LinkCreatorType.Admin => "admin",
        _ => type.ToString().ToLowerInvariant(),
    };
}
