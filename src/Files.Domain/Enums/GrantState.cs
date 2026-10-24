namespace Files.Domain.Enums;

/// <summary>Download grant state (column <c>download_grants.state</c>).</summary>
public enum GrantState
{
    Issued,
    Redeemed,
    Completed,
    Expired,
    Failed,
}
