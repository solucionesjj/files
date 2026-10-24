namespace Files.Domain.Enums;

/// <summary>Download link state (column <c>download_links.status</c>).</summary>
public enum LinkStatus
{
    Active,
    Revoked,
    Expired,
}
