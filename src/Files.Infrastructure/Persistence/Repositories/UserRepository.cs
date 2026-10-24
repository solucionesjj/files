using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
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
}
