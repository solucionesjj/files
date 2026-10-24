using Files.Application.Identity;
using Files.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>Evaluates a <see cref="PortalRoleRequirement"/> from the validated token's roles claim.</summary>
public sealed class PortalRoleRequirementHandler : AuthorizationHandler<PortalRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PortalRoleRequirement requirement)
    {
        var roles = TokenClaims.GetRoles(context.User);
        if (RolePolicy.Satisfies(requirement.RequiredRole, roles))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
