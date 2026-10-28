using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(FilesDbContext db) : IUserRepository
{
    public void Add(User user) => db.Users.Add(user);

    public Task<User?> GetByTenantIdentityAsync(Guid tenantId, Guid objectId, CancellationToken ct = default) =>
        db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.ObjectId == objectId, ct);

    public Task<User?> GetByTenantIdentityTrackedAsync(Guid tenantId, Guid objectId, CancellationToken ct = default) =>
        db.Users
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.ObjectId == objectId, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.Users
            .SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<int> CountByRoleAsync(UserRole role, CancellationToken ct = default) =>
        db.Users.CountAsync(u => u.Role == role, ct);

    public async Task<IReadOnlyList<User>> ListAsync(int limit, DateTimeOffset? cursorCreatedAt, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();

        if (cursorCreatedAt is not null)
        {
            query = query.Where(u => u.CreatedAt < cursorCreatedAt.Value);
        }

        return await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Take(limit)
            .ToListAsync(ct);
    }
}
