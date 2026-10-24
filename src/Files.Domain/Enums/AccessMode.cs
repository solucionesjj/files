namespace Files.Domain.Enums;

/// <summary>File access shape (column <c>files.access_mode</c>).</summary>
public enum AccessMode
{
    OwnerOnly,
    PrivateLink,
    PublicLink,
    IntegrationOnly,
}
