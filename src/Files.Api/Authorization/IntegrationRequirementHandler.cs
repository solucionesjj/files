using Files.Api.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>
/// Enforces an integration authorization rule from the scope claims emitted by
/// <see cref="ApiKeyAuthenticationHandler"/> (key scopes ∩ integration permissions). No database
/// access is needed here: the authentication handler already resolved and scoped the key.
/// </summary>
public sealed class IntegrationRequirementHandler : AuthorizationHandler<IntegrationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IntegrationRequirement requirement)
    {
        var scopes = context.User
            .FindAll(ApiKeyClaims.Scope)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (requirement.RequiredPermissions.IsSubsetOf(scopes))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
