using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class UploadChunkRepository(FilesDbContext db) : IUploadChunkRepository
{
    public Task<UploadChunk?> GetAsync(Guid sessionId, int chunkNumber, CancellationToken ct = default) =>
        db.UploadChunks.AsNoTracking().SingleOrDefaultAsync(
            c => c.UploadSessionId == sessionId && c.ChunkNumber == chunkNumber, ct);

    public async Task<IReadOnlyList<UploadChunk>> ListBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.UploadChunks.AsNoTracking()
            .Where(c => c.UploadSessionId == sessionId)
            .OrderBy(c => c.ChunkNumber)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListReceivedNumbersAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.UploadChunks.AsNoTracking()
            .Where(c => c.UploadSessionId == sessionId)
            .OrderBy(c => c.ChunkNumber)
            .Select(c => c.ChunkNumber)
            .ToListAsync(ct);

    public Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        db.UploadChunks.CountAsync(c => c.UploadSessionId == sessionId, ct);

    public Task DeleteBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        db.UploadChunks.Where(c => c.UploadSessionId == sessionId).ExecuteDeleteAsync(ct);
}
