using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class RetentionPolicyRepository(FilesDbContext db) : IRetentionPolicyRepository
{
    public Task<RetentionPolicy?> GetActiveAsync(CancellationToken ct = default) =>
        db.RetentionPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.IsActive, ct);

    public async Task<IReadOnlyList<RetentionPolicy>> ListAllAsync(CancellationToken ct = default) =>
        await db.RetentionPolicies
            .AsNoTracking()
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<RetentionPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.RetentionPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<RetentionPolicy?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.RetentionPolicies.SingleOrDefaultAsync(p => p.Id == id, ct);

    public void Add(RetentionPolicy policy) => db.RetentionPolicies.Add(policy);
}
