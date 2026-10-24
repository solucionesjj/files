namespace Files.Domain.Enums;

/// <summary>Notification delivery state (column <c>notification_events.status</c>).</summary>
public enum NotificationStatus
{
    Scheduled,
    Sending,
    Accepted,
    Delivered,
    Bounced,
    Failed,
    Suppressed,
    Superseded,
}
