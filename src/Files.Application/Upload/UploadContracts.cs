using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Upload;

/// <summary>Portal upload-session creation input (retention/tags are portal-only).</summary>
public sealed record CreatePortalUpload(
    string FileName,
    long SizeBytes,
    string ContentType,
    string? ChecksumSha256,
    RetentionInput? Retention);

/// <summary>Integration upload-session creation input (always indefinite, integration-only).</summary>
public sealed record CreateIntegrationUpload(
    string FileName,
    long SizeBytes,
    string ContentType,
    string? ChecksumSha256);

/// <summary>Retention fields from the create request (only <c>finite</c> is valid for portal files).</summary>
public sealed record RetentionInput(string? Mode, int? Days, string? Reason);

/// <summary>Response body for a newly created upload session.</summary>
public sealed record UploadSessionCreated(
    Guid FileId,
    Guid UploadSessionId,
    long ChunkSizeBytes,
    int ChunkCount,
    DateTimeOffset ExpiresAt,
    long ReceivedBytes);

/// <summary>Response body for an accepted chunk.</summary>
public sealed record ChunkAccepted(
    Guid UploadSessionId,
    int ChunkNumber,
    long ReceivedBytes,
    int ChunksReceived,
    int ChunkCount,
    UploadSessionStatus Status);

/// <summary>Read model for the upload status/resume endpoint.</summary>
public sealed record UploadStatusView(
    Guid FileId,
    Guid UploadSessionId,
    UploadSessionStatus Status,
    long ChunkSizeBytes,
    int ChunkCount,
    long ReceivedBytes,
    IReadOnlyList<int> ChunksReceived,
    DateTimeOffset ExpiresAt,
    string? LastError);

/// <summary>A finished (available or quarantined) file plus its upload session.</summary>
public sealed record CompletedUpload(FileEntity File, UploadSession Session);
