namespace Files.Domain.Enums;

/// <summary>
/// Files-managed portal role for a human user (ADR-0014). Entra ID authenticates the user; the role
/// lives in <c>users.role</c> and is assigned by an administrator, never derived from a token.
/// </summary>
public enum UserRole
{
    User,

    Admin,

    Auditor,
}
