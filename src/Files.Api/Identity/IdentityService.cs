using System.Security.Claims;
using Files.Api.Authentication;
using Files.Application.Identity;
using Files.Domain.Enums;
using Files.Domain.Identity;

namespace Files.Api.Identity;

/// <summary>
/// Resolves the current authenticated actor. Human tokens are provisioned JIT and their role is
/// read from <c>users.role</c> (ADR-0014); integration requests are authenticated by API key and
/// resolve to their integration id and scopes from the key claims. The result is cached per request.
/// </summary>
public sealed class IdentityService(
    IHttpContextAccessor httpContextAccessor,
    IUserProvisioner userProvisioner) : IIdentityService
{
    private ResolvedActor? _resolved;

    public async ValueTask<ResolvedActor> ResolveAsync(CancellationToken ct = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return _resolved = ResolvedActor.System;
        }

        if (string.Equals(principal.Identity.AuthenticationType, ApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            return _resolved = ResolveApiKey(principal);
        }

        // App-only (client-credentials) tokens are not valid anywhere now (ADR-0014).
        if (TokenClaims.IsAppOnly(principal))
        {
            return _resolved = ResolvedActor.System;
        }

        return _resolved = await ResolveUserAsync(principal, ct);
    }

    private async Task<ResolvedActor> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var tenantId = TokenClaims.GetTenantId(principal);
        var objectId = TokenClaims.GetObjectId(principal);
        if (tenantId is null || objectId is null)
        {
            return ResolvedActor.System;
        }

        var user = await userProvisioner.EnsureUserAsync(
            tenantId.Value,
            objectId.Value,
            TokenClaims.GetDisplayName(principal),
            TokenClaims.GetEmail(principal),
            ct);

        var roles = new[] { RolePolicy.ForRole(user.Role) };
        var type = RolePolicy.ResolveActorType(roles);
        return new ResolvedActor(type, user.Id, true, user, null, roles, new HashSet<string>(StringComparer.Ordinal));
    }

    private static ResolvedActor ResolveApiKey(ClaimsPrincipal principal)
    {
        var integrationId = Guid.TryParse(principal.FindFirst(ApiKeyClaims.IntegrationId)?.Value, out var id)
            ? id
            : (Guid?)null;

        var scopes = principal.FindAll(ApiKeyClaims.Scope)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        return new ResolvedActor(ActorType.Integration, integrationId, true, null, null, [], scopes);
    }
}
