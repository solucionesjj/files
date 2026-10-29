using Files.Domain.Identity;
using Files.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>
/// Requires the caller to hold the <c>admin</c> or <c>auditor</c> role (FR-ADM-005/008). Admins and
/// auditors may read the audit/usage surface; neither may read file content (D-19).
/// </summary>
public sealed class AdminOrAuditorRequirement : IAuthorizationRequirement
{
}

/// <summary>Evaluates <see cref="AdminOrAuditorRequirement"/> from the Files-managed role (ADR-0014).</summary>
public sealed class AdminOrAuditorRequirementHandler(IIdentityService identityService)
    : AuthorizationHandler<AdminOrAuditorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminOrAuditorRequirement requirement)
    {
        var actor = await identityService.ResolveAsync();
        if (actor.IsAuthenticated
            && (actor.Roles.Contains(PortalRoles.Admin, StringComparer.Ordinal)
                || actor.Roles.Contains(PortalRoles.Auditor, StringComparer.Ordinal)))
        {
            context.Succeed(requirement);
        }
    }
}
