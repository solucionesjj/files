using System.Net;
using Files.Domain.Common;
using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Registered machine consumer (<c>integrations</c> table).</summary>
public sealed class Integration : IVersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public IntegrationStatus Status { get; set; } = IntegrationStatus.Active;

    public Guid? EntraClientId { get; set; }

    public Guid? EntraServicePrincipalId { get; set; }

    public IPNetwork[] AllowedIpCidrs { get; set; } = [];

    /// <summary>Granted capabilities, read from <c>integration_permissions</c> (never from tokens).</summary>
    public ICollection<IntegrationPermissionEntry> Permissions { get; set; } = [];

    public DateTimeOffset? DisabledAt { get; set; }

    public Guid? DisabledBy { get; set; }

    public string? DisabledReason { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; } = 1;

    public static Integration Create(string slug, string name, string? description = null) =>
        new()
        {
            Slug = slug,
            Name = name,
            Description = description,
        };
}
