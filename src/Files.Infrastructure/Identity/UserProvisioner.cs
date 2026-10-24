using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;

namespace Files.Infrastructure.Identity;

/// <summary>
/// JIT user provisioning for validated Entra tokens (ADR-0004): creates the <c>users</c> row on
/// first sign-in and refreshes mutable profile fields. <c>last_login_at</c> is updated at most once
/// per minute; email/display name are refreshed whenever they change.
/// </summary>
public sealed class UserProvisioner(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IUserProvisioner
{
    private static readonly TimeSpan LastLoginThrottle = TimeSpan.FromMinutes(1);

    public async Task<User> EnsureUserAsync(
        Guid tenantId,
        Guid objectId,
        string displayName,
        string? email,
        CancellationToken ct = default)
    {
        var existing = await users.GetByTenantIdentityTrackedAsync(tenantId, objectId, ct);
        if (existing is not null)
        {
            var changed = false;

            if (!string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                existing.Email = email;
                changed = true;
            }

            if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
            {
                existing.DisplayName = displayName;
                changed = true;
            }

            var now = timeProvider.GetUtcNow();
            if (existing.LastLoginAt is null || now - existing.LastLoginAt >= LastLoginThrottle)
            {
                existing.LastLoginAt = now;
                changed = true;
            }

            if (changed)
            {
                await unitOfWork.SaveChangesAsync(ct);
            }

            return existing;
        }

        var user = User.Create(tenantId, objectId, displayName, email);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);
        return user;
    }
}
