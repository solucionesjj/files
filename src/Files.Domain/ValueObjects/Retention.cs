using Files.Domain.Enums;

namespace Files.Domain.ValueObjects;

/// <summary>
/// Retention decision for a file. Encodes the invariant from the schema:
/// <c>finite</c> files carry an expiry, <c>indefinite</c> files never expire, and any
/// finite retention in days must stay within the 1..365 absolute window (ADR-0006).
/// </summary>
public sealed record Retention
{
    private Retention(RetentionMode mode, DateTimeOffset? expiresAt, int? days)
    {
        Mode = mode;
        ExpiresAt = expiresAt;
        Days = days;
    }

    public RetentionMode Mode { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public int? Days { get; }

    public bool IsFinite => Mode == RetentionMode.Finite;

    /// <summary>Creates a finite retention with an expiry and a day count in the legal window.</summary>
    public static Retention Finite(DateTimeOffset expiresAt, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, 365);
        return new Retention(RetentionMode.Finite, expiresAt, days);
    }

    /// <summary>Creates an indefinite retention with no expiry and no day count.</summary>
    public static Retention Indefinite() => new(RetentionMode.Indefinite, null, null);
}
