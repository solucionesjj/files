using Microsoft.AspNetCore.Authorization;

namespace Files.Api.Authorization;

/// <summary>
/// Requires a machine caller to hold the <c>Files.Integration</c> role, resolve to an active
/// integration via <c>azp</c>/<c>appid</c>, and — when required permissions are listed — have them
/// granted in <c>integration_permissions</c> (never from token scopes).
/// </summary>
public sealed class IntegrationRequirement : IAuthorizationRequirement
{
    public IntegrationRequirement(params string[] requiredPermissions)
    {
        RequiredPermissions = new HashSet<string>(requiredPermissions, StringComparer.Ordinal);
    }

    public IReadOnlySet<string> RequiredPermissions { get; }
}
