namespace Files.Domain.Entities;

/// <summary>Versioned global retention configuration (<c>retention_policies</c> table).</summary>
public sealed class RetentionPolicy
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public int DefaultDays { get; set; }

    public int MaxDays { get; set; }

    public bool AllowUserOverride { get; set; } = true;

    public bool AllowIndefinite { get; set; }

    public bool PublicLinksEnabled { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset EffectiveFrom { get; set; }

    public string? Notes { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
