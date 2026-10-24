using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Scheduled or attempted expiration notification (<c>notification_events</c> table).</summary>
public sealed class NotificationEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public Guid PolicyId { get; set; }

    public int IntervalDays { get; set; }

    public DateTimeOffset ExpirationAt { get; set; }

    public string? RecipientEmail { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Scheduled;

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
