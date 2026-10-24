using System.Security.Claims;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Enums;
using Files.Domain.Identity;

namespace Files.Api.Identity;

/// <summary>
/// Resolves the current authenticated actor from the validated token. Human tokens are provisioned
/// JIT (via <see cref="IUserProvisioner"/>); machine tokens resolve to their integration and its DB
/// permissions. The result is cached for the lifetime of the request.
/// </summary>
public sealed class IdentityService(
    IHttpContextAccessor httpContextAccessor,
    IUserProvisioner userProvisioner,
    IIntegrationRepository integrationRepository) : IIdentityService
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

        _resolved = TokenClaims.IsAppOnly(principal)
            ? await ResolveIntegrationAsync(principal, ct)
            : await ResolveUserAsync(principal, ct);

        return _resolved;
    }

    private async Task<ResolvedActor> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var tenantId = TokenClaims.GetTenantId(principal);
        var objectId = TokenClaims.GetObjectId(principal);
        if (tenantId is null || objectId is null)
        {
            return ResolvedActor.System;
        }

        var roles = TokenClaims.GetRoles(principal);
        var user = await userProvisioner.EnsureUserAsync(
            tenantId.Value,
            objectId.Value,
            TokenClaims.GetDisplayName(principal),
            TokenClaims.GetEmail(principal),
            ct);

        var type = RolePolicy.ResolveActorType(roles);
        return new ResolvedActor(type, user.Id, true, user, null, roles, new HashSet<string>(StringComparer.Ordinal));
    }

    private async Task<ResolvedActor> ResolveIntegrationAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var clientId = TokenClaims.GetClientId(principal);
        var integration = clientId is null
            ? null
            : await integrationRepository.GetByEntraClientIdAsync(clientId.Value, ct);

        var roles = TokenClaims.GetRoles(principal);
        var permissions = integration?.Permissions
            .Select(p => p.Permission)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        return new ResolvedActor(ActorType.Integration, integration?.Id, true, null, integration, roles, permissions);
    }
}
