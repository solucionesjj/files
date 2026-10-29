namespace Files.Application.Notifications;

/// <summary>
/// Pure notification-timing rules (mirrors the SQL scheduling predicate in
/// <c>NotificationEventRepository</c>). An interval is scheduled only when its <c>due_at</c>
/// (expiry minus <c>interval_days</c>) is still in the future; past-due intervals are skipped so a
/// file uploaded inside the notification window never gets a retroactive email (ADR-0006 §2).
/// </summary>
public static class NotificationTiming
{
    public static DateTimeOffset DueAt(DateTimeOffset expiresAt, int intervalDays) =>
        expiresAt.AddDays(-intervalDays);

    /// <summary>True when the interval's due instant is already past (do not schedule).</summary>
    public static bool IsRetroactive(DateTimeOffset expiresAt, int intervalDays, DateTimeOffset now) =>
        DueAt(expiresAt, intervalDays) <= now;

    /// <summary>True when at least one interval is still due in the future (worth scheduling).</summary>
    public static bool HasAnyFutureDue(DateTimeOffset expiresAt, IReadOnlyCollection<int> intervalsDays, DateTimeOffset now) =>
        intervalsDays.Any(days => !IsRetroactive(expiresAt, days, now));
}
