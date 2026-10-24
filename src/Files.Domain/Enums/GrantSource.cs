namespace Files.Domain.Enums;

/// <summary>How a download grant was issued (column <c>download_grants.source</c>).</summary>
public enum GrantSource
{
    Owner,
    PrivateLink,
    PublicLink,
}
