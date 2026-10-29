namespace Files.Application.Notifications;

/// <summary>A due notification claimed by the sender, with the file/owner context needed to render.</summary>
public sealed record NotificationWorkItem(
    Guid EventId,
    Guid FileId,
    string? RecipientEmail,
    int IntervalDays,
    DateTimeOffset ExpirationAt,
    string FileName,
    string OwnerName,
    int Attempts);
