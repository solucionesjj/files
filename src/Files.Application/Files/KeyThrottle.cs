using Files.Application.Configuration;

namespace Files.Application.Files;

/// <summary>
/// Abuse-control policy for the 8-character download key (FR-LINK-009 / ADR-0005). The counters are
/// PostgreSQL-backed (so all replicas share them); this class only encodes the thresholds and
/// delays so the state machine is unit-testable without a database. Thresholds come from the
/// operator <c>Abuse__*</c> keys (canonical defaults: 5/15 min/15 min/100 per hour); the progressive
/// delays are fixed at 0.5/1/2/4 s.
/// </summary>
public sealed class KeyThrottle
{
    public KeyThrottle(
        int maxAttemptsPerWindow,
        TimeSpan window,
        TimeSpan linkIpLockout,
        int softLockThresholdPerHour,
        TimeSpan softLock)
    {
        MaxAttemptsPerWindow = maxAttemptsPerWindow;
        Window = window;
        LinkIpLockout = linkIpLockout;
        SoftLockThresholdPerHour = softLockThresholdPerHour;
        SoftLock = softLock;
    }

    public int MaxAttemptsPerWindow { get; }

    public TimeSpan Window { get; }

    public TimeSpan LinkIpLockout { get; }

    public int SoftLockThresholdPerHour { get; }

    public TimeSpan SoftLock { get; }

    /// <summary>Sliding window over which the per-link (all-IP) soft-lock threshold is measured.</summary>
    public static TimeSpan SoftLockWindow { get; } = TimeSpan.FromHours(1);

    public static KeyThrottle FromOptions(LinksOptions options) => new(
        options.MaxKeyAttemptsPerWindow,
        TimeSpan.FromMinutes(options.KeyAttemptWindowMinutes),
        TimeSpan.FromMinutes(options.KeyLockoutMinutes),
        options.MaxFailuresPerLinkPerHour,
        TimeSpan.FromMinutes(options.KeyLockoutMinutes));

    /// <summary>True when either the per-link+IP or the link-level lockout is still in effect.</summary>
    public static bool IsLockedOut(
        DateTimeOffset? linkIpLockedUntil,
        DateTimeOffset? linkSoftLockedUntil,
        DateTimeOffset now) =>
        (linkIpLockedUntil is { } ip && ip > now) || (linkSoftLockedUntil is { } link && link > now);

    /// <summary>Progressive delay for the Nth failed attempt in the window (1-based), capped at 4 s.</summary>
    public static TimeSpan DelayForAttempt(int failedAttemptsInWindow) =>
        failedAttemptsInWindow switch
        {
            <= 0 => TimeSpan.Zero,
            1 => TimeSpan.FromMilliseconds(500),
            2 => TimeSpan.FromSeconds(1),
            3 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(4),
        };

    /// <summary>True when the failures in the window warrant a new per-link+IP lockout.</summary>
    public bool ShouldLockOutLinkIp(int failedAttemptsInWindow) =>
        failedAttemptsInWindow >= MaxAttemptsPerWindow;

    /// <summary>True when per-hour failures across all IPs warrant a link-level soft lock.</summary>
    public bool ShouldSoftLockLink(int linkFailuresPerHour) =>
        linkFailuresPerHour >= SoftLockThresholdPerHour;
}
