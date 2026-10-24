using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Enums;
using Files.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>
/// Enforces the integration authorization rule: <c>Files.Integration</c> role + active integration
/// bound to the token's <c>azp</c>/<c>appid</c> + the required permissions granted in the database.
/// </summary>
public sealed class IntegrationRequirementHandler(IIntegrationRepository integrations)
    : AuthorizationHandler<IntegrationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IntegrationRequirement requirement)
    {
        var roles = TokenClaims.GetRoles(context.User);
        if (!roles.Contains(PortalRoles.Integration, StringComparer.Ordinal))
        {
            return;
        }

        var clientId = TokenClaims.GetClientId(context.User);
        if (clientId is null)
        {
            return;
        }

        var integration = await integrations.GetByEntraClientIdAsync(clientId.Value);
        if (integration is null || integration.Status != IntegrationStatus.Active)
        {
            return;
        }

        var granted = integration.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        if (!requirement.RequiredPermissions.IsSubsetOf(granted))
        {
            return;
        }

        context.Succeed(requirement);
    }
}
