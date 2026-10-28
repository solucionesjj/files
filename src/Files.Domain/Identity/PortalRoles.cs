namespace Files.Domain.Identity;

/// <summary>
/// Files-managed portal role names (ADR-0014). The role is read from <c>users.role</c>
/// (<c>user</c>/<c>admin</c>/<c>auditor</c>) on every request; Entra app roles are not used.
/// </summary>
public static class PortalRoles
{
    public const string User = "user";

    public const string Admin = "admin";

    public const string Auditor = "auditor";

    /// <summary>The application role that proves a caller is a registered machine app.</summary>
    public const string Integration = "Files.Integration";
}
