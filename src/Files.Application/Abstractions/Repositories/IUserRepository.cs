using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for human identities. Lookups are always tenant-scoped.</summary>
public interface IUserRepository
{
    void Add(User user);

    /// <summary>Returns the user for the Entra tenant/object pair, or <c>null</c> (JIT provisioning).</summary>
    Task<User?> GetByTenantIdentityAsync(Guid tenantId, Guid objectId, CancellationToken ct = default);

    /// <summary>Tracked variant used by JIT provisioning to update profile fields/last login.</summary>
    Task<User?> GetByTenantIdentityTrackedAsync(Guid tenantId, Guid objectId, CancellationToken ct = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant for admin role changes.</summary>
    Task<User?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    /// <summary>Number of users holding the given role (used to protect the last admin).</summary>
    Task<int> CountByRoleAsync(UserRole role, CancellationToken ct = default);

    /// <summary>Keyset-paginated listing ordered by <c>(created_at DESC, id DESC)</c>.</summary>
    Task<IReadOnlyList<User>> ListAsync(int limit, DateTimeOffset? cursorCreatedAt, CancellationToken ct = default);
}
