namespace Files.Application.Deletion;

/// <summary>
/// Deterministic exponential backoff for physical deletion retries (FR-DEL-004 / ADR-0008):
/// <c>min(1h, 1m * 2^(attempts-1))</c> with ±20% uniform jitter.
/// </summary>
public static class DeletionBackoff
{
    public const double JitterFraction = 0.2;
    private static readonly TimeSpan Cap = TimeSpan.FromHours(1);

    /// <summary>The uncapped base delay for a 1-based <paramref name="attempt"/> (no jitter).</summary>
    public static TimeSpan BaseDelay(int attempt)
    {
        var shifted = Math.Max(attempt - 1, 0);
        // 2^6 minutes = 64 m already exceeds the 1 h cap, so clamp the shift.
        shifted = Math.Min(shifted, 6);
        var delay = TimeSpan.FromMinutes(1L << shifted);
        return delay > Cap ? Cap : delay;
    }

    /// <summary>The jittered delay for a 1-based <paramref name="attempt"/> (capped at 1 h).</summary>
    public static TimeSpan BackoffDelay(int attempt, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var baseDelay = BaseDelay(attempt);
        var factor = 1.0 + ((random.NextDouble() * 2.0) - 1.0) * JitterFraction;
        var jittered = TimeSpan.FromTicks((long)(baseDelay.Ticks * factor));
        return jittered < TimeSpan.Zero ? TimeSpan.Zero : jittered;
    }
}
