using Files.Domain.Common;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;

namespace Files.Domain.Entities;

/// <summary>File metadata, ownership, lifecycle, and retention (<c>files</c> table).</summary>
public sealed class FileEntity : IVersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public OwnerType OwnerType { get; set; }

    public Guid? OwnerUserId { get; set; }

    public Guid? OwnerIntegrationId { get; set; }

    public string OriginalFilename { get; set; } = string.Empty;

    public StorageProvider StorageProvider { get; set; } = StorageProvider.S3;

    public string StorageBucket { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public ContentTypeSource ContentTypeSource { get; set; } = ContentTypeSource.Declared;

    public string? ChecksumSha256 { get; set; }

    public FileStatus Status { get; set; } = FileStatus.PendingUpload;

    public AccessMode AccessMode { get; set; } = AccessMode.OwnerOnly;

    public RetentionMode RetentionMode { get; set; } = RetentionMode.Finite;

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? RetentionDays { get; set; }

    public Guid? RetentionPolicyId { get; set; }

    public ScanStatus ScanStatus { get; set; } = ScanStatus.NotScanned;

    public DateTimeOffset? ScanCompletedAt { get; set; }

    public int ScanAttempts { get; set; }

    public DateTimeOffset? ScanNextAttemptAt { get; set; }

    public string? QuarantineReason { get; set; }

    public bool LegalHold { get; set; }

    public string? LegalHoldReason { get; set; }

    public DateTimeOffset? LegalHoldSetAt { get; set; }

    public Guid? LegalHoldSetBy { get; set; }

    public DateTimeOffset? LegalHoldReviewAt { get; set; }

    public string? RetentionReason { get; set; }

    public long SuccessfulDownloads { get; set; }

    public DateTimeOffset? LastDownloadAt { get; set; }

    public string Metadata { get; set; } = "{}";

    public DateTimeOffset? AvailableAt { get; set; }

    public DeletionRequesterType? DeletionRequestedByType { get; set; }

    public Guid? DeletionRequestedBy { get; set; }

    public string? DeletionReason { get; set; }

    public int DeletionAttempts { get; set; }

    public DateTimeOffset? NextDeletionAt { get; set; }

    public string? LastDeletionError { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; } = 1;

    /// <summary>Creates a human-owned file with the given retention decision.</summary>
    public static FileEntity CreateHuman(
        Guid ownerUserId,
        string originalFilename,
        string storageBucket,
        string storageKey,
        long sizeBytes,
        string contentType,
        Retention retention)
    {
        var file = new FileEntity
        {
            OwnerType = OwnerType.Human,
            OwnerUserId = ownerUserId,
            OriginalFilename = originalFilename,
            StorageBucket = storageBucket,
            StorageKey = storageKey,
            SizeBytes = sizeBytes,
            ContentType = contentType,
        };
        file.ApplyRetention(retention);
        return file;
    }

    /// <summary>Creates an integration-owned file (always integration-only and indefinite).</summary>
    public static FileEntity CreateIntegration(
        Guid ownerIntegrationId,
        string originalFilename,
        string storageBucket,
        string storageKey,
        long sizeBytes,
        string contentType)
    {
        return new FileEntity
        {
            OwnerType = OwnerType.Integration,
            OwnerIntegrationId = ownerIntegrationId,
            AccessMode = AccessMode.IntegrationOnly,
            OriginalFilename = originalFilename,
            StorageBucket = storageBucket,
            StorageKey = storageKey,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            RetentionMode = RetentionMode.Indefinite,
            ExpiresAt = null,
            RetentionDays = null,
        };
    }

    public void ApplyRetention(Retention retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        RetentionMode = retention.Mode;
        ExpiresAt = retention.ExpiresAt;
        RetentionDays = retention.Days;
    }
}
