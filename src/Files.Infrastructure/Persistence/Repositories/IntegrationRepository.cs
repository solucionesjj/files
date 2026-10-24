using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class IntegrationRepository(FilesDbContext db) : IIntegrationRepository
{
    public void Add(Integration integration) => db.Integrations.Add(integration);

    public Task<Integration?> GetByEntraClientIdAsync(Guid clientId, CancellationToken ct = default) =>
        WithPermissions()
            .SingleOrDefaultAsync(i => i.EntraClientId == clientId, ct);

    public Task<Integration?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Integrations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Slug == slug, ct);

    public Task<Integration?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        WithPermissions()
            .SingleOrDefaultAsync(i => i.Id == id, ct);

    public Task<Integration?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.Integrations
            .Include(i => i.Permissions)
            .SingleOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<Integration>> ListAsync(
        IntegrationStatus? status,
        int limit,
        DateTimeOffset? cursorCreatedAt,
        CancellationToken ct = default)
    {
        var query = WithPermissions();

        if (status is not null)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        // Keyset on created_at DESC (id is a deterministic tiebreaker in the ORDER BY). Integrations
        // are created one per transaction, so `now()` values are unique in practice; the id tiebreak
        // keeps the ordering stable without needing an untranslatable Guid inequality.
        if (cursorCreatedAt is not null)
        {
            query = query.Where(i => i.CreatedAt < cursorCreatedAt.Value);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    private IQueryable<Integration> WithPermissions() =>
        db.Integrations
            .AsNoTracking()
            .Include(i => i.Permissions);
}
