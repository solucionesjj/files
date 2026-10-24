using Files.Domain.Common;
using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Human identity mapped from Entra ID (<c>users</c> table).</summary>
public sealed class User : IVersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid ObjectId { get; set; }

    public string? Email { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; } = 1;

    public static User Create(Guid tenantId, Guid objectId, string displayName, string? email = null) =>
        new()
        {
            TenantId = tenantId,
            ObjectId = objectId,
            DisplayName = displayName,
            Email = email,
        };
}
