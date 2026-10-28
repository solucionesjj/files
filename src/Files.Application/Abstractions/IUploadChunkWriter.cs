using Files.Domain.Enums;

namespace Files.Application.Abstractions;

/// <summary>Outcome of atomically claiming a chunk and advancing the session's received bytes.</summary>
public sealed record ChunkWriteResult(bool Inserted, long ReceivedBytes, UploadSessionStatus Status);

/// <summary>
/// Performs the concurrency-sensitive chunk claim: an idempotent insert of the chunk row plus an
/// atomic increment of <c>received_bytes</c> (and the <c>received</c> status transition) in the
/// ambient transaction. Mirrors the SQL in the data model (§5 "Chunk write and completion").
/// </summary>
public interface IUploadChunkWriter
{
    Task<ChunkWriteResult> WriteChunkAsync(
        Guid sessionId,
        int chunkNumber,
        long offsetBytes,
        long sizeBytes,
        string? checksumSha256,
        CancellationToken ct = default);
}
