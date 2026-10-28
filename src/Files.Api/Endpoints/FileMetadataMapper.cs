using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

/// <summary>File metadata response shape (OpenAPI <c>FileMetadata</c>).</summary>
public sealed record FileMetadataResponse(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    string Status,
    string AccessMode,
    FileRetentionResponse Retention,
    IReadOnlyList<string> Tags,
    FileDownloadStatsResponse Downloads,
    string ScanStatus,
    bool LegalHold,
    string? LegalHoldReason,
    DateTimeOffset? LegalHoldReviewAt,
    FileOwnerResponse Owner,
    string? QuarantineReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? DeletedAt,
    int Version,
    string Etag);

public sealed record FileRetentionResponse(string Mode, int? Days, DateTimeOffset? ExpiresAt, string? Reason);

public sealed record FileDownloadStatsResponse(long Count, DateTimeOffset? LastAt);

public sealed record FileOwnerResponse(string Type, string? DisplayName);

public static class FileMetadataMapper
{
    public static FileMetadataResponse ToResponse(FileEntity file, IReadOnlyList<string> tags) => new(
        file.Id,
        file.OriginalFilename,
        file.SizeBytes,
        file.ContentType,
        StatusName(file.Status),
        AccessModeName(file.AccessMode),
        new FileRetentionResponse(
            RetentionModeName(file.RetentionMode),
            file.RetentionDays,
            file.ExpiresAt,
            file.RetentionReason),
        tags,
        new FileDownloadStatsResponse(file.SuccessfulDownloads, file.LastDownloadAt),
        ScanStatusName(file.ScanStatus),
        file.LegalHold,
        file.LegalHoldReason,
        file.LegalHoldReviewAt,
        new FileOwnerResponse(OwnerTypeName(file.OwnerType), null),
        file.QuarantineReason,
        file.CreatedAt,
        file.UpdatedAt,
        file.AvailableAt,
        file.DeletedAt,
        file.Version,
        $"\"{file.Version}\"");

    private static string StatusName(FileStatus status) => status switch
    {
        FileStatus.PendingUpload => "pending_upload",
        FileStatus.Processing => "processing",
        FileStatus.Available => "available",
        FileStatus.Quarantined => "quarantined",
        FileStatus.Expired => "expired",
        FileStatus.PendingDeletion => "pending_deletion",
        FileStatus.Deleted => "deleted",
        FileStatus.UploadFailed => "upload_failed",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string AccessModeName(AccessMode mode) => mode switch
    {
        AccessMode.OwnerOnly => "owner_only",
        AccessMode.PrivateLink => "private_link",
        AccessMode.PublicLink => "public_link",
        AccessMode.IntegrationOnly => "integration_only",
        _ => mode.ToString().ToLowerInvariant(),
    };

    private static string RetentionModeName(RetentionMode mode) => mode switch
    {
        RetentionMode.Finite => "finite",
        RetentionMode.Indefinite => "indefinite",
        _ => mode.ToString().ToLowerInvariant(),
    };

    private static string ScanStatusName(ScanStatus status) => status switch
    {
        ScanStatus.NotScanned => "not_scanned",
        ScanStatus.Pending => "pending",
        ScanStatus.Clean => "clean",
        ScanStatus.Infected => "infected",
        ScanStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string OwnerTypeName(OwnerType type) => type switch
    {
        OwnerType.Human => "human",
        OwnerType.Integration => "integration",
        _ => type.ToString().ToLowerInvariant(),
    };
}
