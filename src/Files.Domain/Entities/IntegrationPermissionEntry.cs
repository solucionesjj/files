namespace Files.Domain.Entities;

/// <summary>One granted permission for an integration (<c>integration_permissions</c> table).</summary>
public sealed class IntegrationPermissionEntry
{
    public Guid IntegrationId { get; set; }

    public string Permission { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
