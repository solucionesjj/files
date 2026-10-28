using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class IntegrationApiKeyRepository(FilesDbContext db) : IIntegrationApiKeyRepository
{
    public void Add(IntegrationApiKey key) => db.IntegrationApiKeys.Add(key);

    public Task<IntegrationApiKey?> GetByPrefixAsync(string prefix, CancellationToken ct = default) =>
        db.IntegrationApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(k => k.Prefix == prefix, ct);

    public Task<IntegrationApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.IntegrationApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(k => k.Id == id, ct);

    public Task<IntegrationApiKey?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.IntegrationApiKeys
            .SingleOrDefaultAsync(k => k.Id == id, ct);

    public async Task<IReadOnlyList<IntegrationApiKey>> ListByIntegrationAsync(Guid integrationId, CancellationToken ct = default) =>
        await db.IntegrationApiKeys
            .AsNoTracking()
            .Where(k => k.IntegrationId == integrationId)
            .OrderByDescending(k => k.CreatedAt)
            .ThenByDescending(k => k.Id)
            .ToListAsync(ct);

    public Task UpdateLastUsedAsync(Guid id, DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default) =>
        db.IntegrationApiKeys
            .Where(k => k.Id == id && (k.LastUsedAt == null || k.LastUsedAt < cutoff))
            .ExecuteUpdateAsync(setters => setters.SetProperty(k => k.LastUsedAt, now), ct);
}
