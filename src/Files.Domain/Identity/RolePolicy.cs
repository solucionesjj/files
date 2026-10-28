using Files.Domain.Enums;

namespace Files.Domain.Identity;

/// <summary>
/// Pure portal-role policy mapping (RBAC). Roles come from <c>users.role</c> and are re-evaluated
/// per request (ADR-0014); Entra app roles and the token's <c>roles</c> claim are ignored.
/// </summary>
public static class RolePolicy
{
    /// <summary>Resolves the actor type for a human principal (integration tokens are handled separately).</summary>
    public static ActorType ResolveActorType(IReadOnlyCollection<string> roles) =>
        roles.Contains(PortalRoles.Admin, StringComparer.Ordinal) ? ActorType.Admin : ActorType.User;

    /// <summary>Maps a Files-managed <see cref="UserRole"/> to its portal role name (ADR-0014).</summary>
    public static string ForRole(UserRole role) => role switch
    {
        UserRole.Admin => PortalRoles.Admin,
        UserRole.Auditor => PortalRoles.Auditor,
        _ => PortalRoles.User,
    };

    /// <summary>
    /// Whether <paramref name="roles"/> satisfies <paramref name="requiredRole"/>. A user policy is
    /// satisfied by <c>admin</c> as well (admins inherit user capabilities); the auditor role
    /// is independent and never implies user access.
    /// </summary>
    public static bool Satisfies(string requiredRole, IReadOnlyCollection<string> roles)
    {
        if (string.Equals(requiredRole, PortalRoles.User, StringComparison.Ordinal))
        {
            return roles.Contains(PortalRoles.User, StringComparer.Ordinal)
                || roles.Contains(PortalRoles.Admin, StringComparer.Ordinal);
        }

        if (string.Equals(requiredRole, PortalRoles.Admin, StringComparison.Ordinal))
        {
            return roles.Contains(PortalRoles.Admin, StringComparer.Ordinal);
        }

        if (string.Equals(requiredRole, PortalRoles.Auditor, StringComparison.Ordinal))
        {
            return roles.Contains(PortalRoles.Auditor, StringComparer.Ordinal);
        }

        return false;
    }
}
