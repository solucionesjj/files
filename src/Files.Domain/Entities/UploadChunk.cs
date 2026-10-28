namespace Files.Domain.Entities;

/// <summary>
/// A chunk of an upload session that the API has received into the local staging file
/// (<c>upload_chunks</c> table). The composite key is the session plus the 1-based chunk number.
/// </summary>
public sealed class UploadChunk
{
    public Guid UploadSessionId { get; set; }

    public int ChunkNumber { get; set; }

    public long OffsetBytes { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Optional SHA-256 of the chunk bytes supplied by the client (64 hex chars).</summary>
    public string? ChecksumSha256 { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
