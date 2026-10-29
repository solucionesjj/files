namespace Files.Application.Notifications;

/// <summary>Deterministic retry backoff for transient notification failures (ADR-0009: 1m, 5m, 15m, 1h, 6h).</summary>
public static class NotificationRetry
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];

    /// <summary>Delay before retry <paramref name="attempt"/> (1-based; capped at the last entry).</summary>
    public static TimeSpan BackoffDelay(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, Delays.Length - 1);
        return Delays[index];
    }
}
