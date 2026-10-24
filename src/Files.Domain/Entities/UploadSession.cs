using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Upload intent and chunk geometry (<c>upload_sessions</c> table).</summary>
public sealed class UploadSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public UploadInitiatorType InitiatorType { get; set; }

    public Guid InitiatorId { get; set; }

    public UploadSessionStatus Status { get; set; } = UploadSessionStatus.Initiated;

    public long ExpectedSizeBytes { get; set; }

    public string ExpectedContentType { get; set; } = string.Empty;

    public string? ExpectedChecksumSha256 { get; set; }

    public long ChunkSizeBytes { get; set; }

    public int ChunkCount { get; set; }

    public long ReceivedBytes { get; set; }

    public string StagingPath { get; set; } = string.Empty;

    public string? S3UploadId { get; set; }

    public long? S3PartSizeBytes { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
