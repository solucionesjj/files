namespace Files.Domain.Entities;

/// <summary>Versioned expiration-reminder schedule (<c>notification_policies</c> table).</summary>
public sealed class NotificationPolicy
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public int[] IntervalsDays { get; set; } = [7, 3, 1];

    public string TemplateKey { get; set; } = "file.expiring";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset EffectiveFrom { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
