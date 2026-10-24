using Files.Domain.Entities;

namespace Files.Application.Identity;

/// <summary>
/// Ensures a human identity exists for a validated Entra token (JIT provisioning): creates the
/// <c>users</c> row on first sign-in and refreshes mutable profile attributes with throttling.
/// </summary>
public interface IUserProvisioner
{
    /// <summary>
    /// Returns the existing user or creates one for <paramref name="tenantId"/>/<paramref
    /// name="objectId"/>. Updates <c>email</c>/<c>display_name</c> when they change and
    /// <c>last_login_at</c> at most once per minute.
    /// </summary>
    Task<User> EnsureUserAsync(
        Guid tenantId,
        Guid objectId,
        string displayName,
        string? email,
        CancellationToken ct = default);
}
