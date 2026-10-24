using System.Security.Claims;
using Files.Domain.Identity;

namespace Files.Application.Identity;

/// <summary>
/// Parses the Entra ID v2 claims that the JWT bearer handler exposes when
/// <c>MapInboundClaims = false</c> (short claim names). Pure functions, unit-testable without a
/// host.
/// </summary>
public static class TokenClaims
{
    public const string Tid = "tid";
    public const string Oid = "oid";
    public const string Name = "name";
    public const string Email = "email";
    public const string PreferredUsername = "preferred_username";
    public const string Roles = "roles";
    public const string Azp = "azp";
    public const string AppId = "appid";
    public const string IdTyp = "idtyp";
    public const string Scp = "scp";

    private const string IdTypApp = "app";

    public static Guid? GetTenantId(ClaimsPrincipal principal) => GetGuid(principal, Tid);

    public static Guid? GetObjectId(ClaimsPrincipal principal) => GetGuid(principal, Oid);

    /// <summary>The app-only token's client id from <c>azp</c> (preferred) or <c>appid</c>.</summary>
    public static Guid? GetClientId(ClaimsPrincipal principal) =>
        GetGuid(principal, Azp) ?? GetGuid(principal, AppId);

    public static IReadOnlyList<string> GetRoles(ClaimsPrincipal principal) =>
        principal.FindAll(Roles).Select(c => c.Value).ToArray();

    /// <summary>True when the token is an app-only (client-credentials) token.</summary>
    public static bool IsAppOnly(ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirst(IdTyp)?.Value, IdTypApp, StringComparison.Ordinal);

    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        var scp = principal.FindFirst(Scp)?.Value;
        if (string.IsNullOrWhiteSpace(scp))
        {
            return false;
        }

        return scp.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(s => string.Equals(s, scope, StringComparison.Ordinal));
    }

    /// <summary>
    /// Notification recipient. Prefers <c>preferred_username</c> when it looks like an address,
    /// then <c>email</c>; otherwise <c>null</c> (provisioning still succeeds without an address).
    /// </summary>
    public static string? GetEmail(ClaimsPrincipal principal)
    {
        var preferred = principal.FindFirst(PreferredUsername)?.Value;
        if (!string.IsNullOrWhiteSpace(preferred) && preferred.Contains('@', StringComparison.Ordinal))
        {
            return preferred;
        }

        return principal.FindFirst(Email)?.Value;
    }

    public static string GetDisplayName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirst(Name)?.Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return GetEmail(principal) ?? "Unknown";
    }

    private static Guid? GetGuid(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirst(type)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Classifies a validated principal as a human or machine token. Human tokens are delegated
    /// (not app-only); machine tokens are app-only and carry the <see cref="PortalRoles.Integration"/>
    /// role.
    /// </summary>
    public static TokenKind Classify(ClaimsPrincipal principal)
    {
        if (IsAppOnly(principal))
        {
            return TokenKind.Integration;
        }

        return TokenKind.User;
    }
}

public enum TokenKind
{
    /// <summary>Delegated user token (portal/admin).</summary>
    User,

    /// <summary>App-only client-credentials token (integration API).</summary>
    Integration,
}
