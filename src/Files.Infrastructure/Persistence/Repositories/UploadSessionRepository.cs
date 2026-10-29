using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class UploadSessionRepository(FilesDbContext db) : IUploadSessionRepository
{
    private static readonly UploadSessionStatus[] OpenStatuses =
    [
        UploadSessionStatus.Initiated,
        UploadSessionStatus.Uploading,
        UploadSessionStatus.Received,
        UploadSessionStatus.Committing,
    ];

    public void Add(UploadSession session) => db.UploadSessions.Add(session);

    public Task<UploadSession?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<UploadSession?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.UploadSessions.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<UploadSession?> GetOwnedByUserAsync(Guid id, Guid userId, CancellationToken ct = default) =>
        db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(
            s => s.Id == id && s.InitiatorType == UploadInitiatorType.User && s.InitiatorId == userId, ct);

    public Task<UploadSession?> GetOwnedByIntegrationAsync(Guid id, Guid integrationId, CancellationToken ct = default) =>
        db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(
            s => s.Id == id && s.InitiatorType == UploadInitiatorType.Integration && s.InitiatorId == integrationId, ct);

    public Task<int> CountOpenAsync(DateTimeOffset now, CancellationToken ct = default) =>
        db.UploadSessions.CountAsync(
            s => OpenStatuses.Contains(s.Status) && s.ExpiresAt > now, ct);

    public Task<int> CountOpenByIntegrationAsync(Guid integrationId, DateTimeOffset now, CancellationToken ct = default) =>
        db.UploadSessions.CountAsync(
            s => s.InitiatorType == UploadInitiatorType.Integration
                 && s.InitiatorId == integrationId
                 && OpenStatuses.Contains(s.Status)
                 && s.ExpiresAt > now, ct);

    public async Task<IReadOnlyList<UploadSession>> ListExpiredOpenAsync(DateTimeOffset now, int limit, CancellationToken ct = default) =>
        await db.UploadSessions.AsNoTracking()
            .Where(s => OpenStatuses.Contains(s.Status) && s.ExpiresAt <= now)
            .OrderBy(s => s.ExpiresAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UploadSession>> ListStaleCommittingAsync(DateTimeOffset staleBefore, int limit, CancellationToken ct = default) =>
        await db.UploadSessions.AsNoTracking()
            .Where(s => s.Status == UploadSessionStatus.Committing && s.UpdatedAt < staleBefore)
            .OrderBy(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListOpenStagingPathsAsync(CancellationToken ct = default) =>
        await db.UploadSessions.AsNoTracking()
            .Where(s => OpenStatuses.Contains(s.Status))
            .Select(s => s.StagingPath)
            .ToListAsync(ct);
}
