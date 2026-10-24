using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>Requires the caller's <c>roles</c> claim to satisfy a portal role (see RolePolicy).</summary>
public sealed class PortalRoleRequirement(string requiredRole) : IAuthorizationRequirement
{
    public string RequiredRole { get; } = requiredRole;
}
