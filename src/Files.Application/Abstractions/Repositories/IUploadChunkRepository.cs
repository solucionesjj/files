using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for received upload chunks.</summary>
public interface IUploadChunkRepository
{
    Task<UploadChunk?> GetAsync(Guid sessionId, int chunkNumber, CancellationToken ct = default);

    Task<IReadOnlyList<UploadChunk>> ListBySessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>The received chunk numbers, ordered ascending (for resume/status).</summary>
    Task<IReadOnlyList<int>> ListReceivedNumbersAsync(Guid sessionId, CancellationToken ct = default);

    Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default);

    Task DeleteBySessionAsync(Guid sessionId, CancellationToken ct = default);
}
