namespace Files.Domain.Identity;

/// <summary>
/// Entra ID app-role claim values. Roles are evaluated per request from the validated JWT
/// <c>roles</c> claim and are never persisted (ADR-0004).
/// </summary>
public static class PortalRoles
{
    public const string User = "Files.User";

    public const string Admin = "Files.Admin";

    public const string Auditor = "Files.Auditor";

    /// <summary>The application role that proves a caller is a registered machine app.</summary>
    public const string Integration = "Files.Integration";
}
