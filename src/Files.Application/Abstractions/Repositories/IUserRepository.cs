using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for human identities. Lookups are always tenant-scoped.</summary>
public interface IUserRepository
{
    void Add(User user);

    /// <summary>Returns the user for the Entra tenant/object pair, or <c>null</c> (JIT provisioning).</summary>
    Task<User?> GetByTenantIdentityAsync(Guid tenantId, Guid objectId, CancellationToken ct = default);
}
