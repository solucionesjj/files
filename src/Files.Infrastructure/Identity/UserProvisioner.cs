using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace Files.Infrastructure.Identity;

/// <summary>
/// JIT user provisioning for validated Entra tokens (ADR-0014): creates the <c>users</c> row on
/// first sign-in and refreshes mutable profile fields. <c>last_login_at</c> is updated at most once
/// per minute; email/display name are refreshed whenever they change. A user whose email matches
/// <c>Auth__BootstrapAdminEmail</c> is promoted to <c>admin</c> (idempotent, never demotes).
/// </summary>
public sealed class UserProvisioner(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IConfiguration configuration) : IUserProvisioner
{
    private static readonly TimeSpan LastLoginThrottle = TimeSpan.FromMinutes(1);

    private readonly string? _bootstrapAdminEmail = NormalizeEmail(configuration["Auth:BootstrapAdminEmail"]);

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

            changed |= PromoteIfBootstrapAdmin(existing);

            if (changed)
            {
                await unitOfWork.SaveChangesAsync(ct);
            }

            return existing;
        }

        var user = User.Create(tenantId, objectId, displayName, email);
        PromoteIfBootstrapAdmin(user);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);
        return user;
    }

    private bool PromoteIfBootstrapAdmin(User user)
    {
        if (user.Role != UserRole.User
            || _bootstrapAdminEmail is null
            || !string.Equals(user.Email, _bootstrapAdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        user.Role = UserRole.Admin;
        return true;
    }

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
