namespace Files.Domain.Identity;

/// <summary>
/// Machine permissions stored on <c>integration_permissions</c> and enforced from the database,
/// never from token scopes (ADR-0004). The <c>Files.Integration</c> role only proves the app was
/// granted the API permission; the concrete capabilities below are the real authorization source.
/// </summary>
public static class IntegrationPermissions
{
    public const string FileWrite = "file.write";

    public const string FileRead = "file.read";

    public const string FileDelete = "file.delete";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { FileWrite, FileRead, FileDelete };
}
