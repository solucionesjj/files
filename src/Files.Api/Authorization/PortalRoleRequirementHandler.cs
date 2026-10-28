using Files.Application.Identity;
using Files.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>Evaluates a <see cref="PortalRoleRequirement"/> from the Files-managed role (ADR-0014).</summary>
public sealed class PortalRoleRequirementHandler(IIdentityService identityService)
    : AuthorizationHandler<PortalRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PortalRoleRequirement requirement)
    {
        var actor = await identityService.ResolveAsync();
        if (actor.IsAuthenticated && RolePolicy.Satisfies(requirement.RequiredRole, actor.Roles))
        {
            context.Succeed(requirement);
        }
    }
}
