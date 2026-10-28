using Files.Application.Abstractions;
using Files.Domain.ValueObjects;

namespace Files.Application.Files;

/// <summary>
/// Pure retention validation for human (portal) files (ADR-0006): finite only, within the absolute
/// 1..365 window, with a mandatory note (≥10 chars) when extending beyond the current default.
/// Legal hold blocks any retention change. Kept free of ASP.NET types for unit testing.
/// </summary>
public static class RetentionRules
{
    public const int DefaultDays = 30;
    public const int MaxDays = 365;
    public const int MinReasonLength = 10;

    /// <summary>
    /// Validates a requested finite retention and resolves the concrete expiry (relative to
    /// <paramref name="now"/>, the server clock). Returns a <see cref="ServiceError"/> on failure
    /// and a resolved <see cref="Retention"/> on success.
    /// </summary>
    public static ServiceError? ResolveFinite(
        int? days,
        string? reason,
        bool legalHold,
        DateTimeOffset now,
        out Retention? retention,
        int defaultDays = DefaultDays,
        int maxDays = MaxDays)
    {
        retention = null;

        if (legalHold)
        {
            return new ServiceError(409, "legal_hold_active", "Legal hold blocks retention changes");
        }

        var requested = days ?? defaultDays;
        if (requested is < 1 || requested > maxDays)
        {
            return new ServiceError(422, "retention_exceeds_maximum", $"Retention must be between 1 and {maxDays} days");
        }

        var trimmedReason = reason?.Trim();
        if (requested > defaultDays && (string.IsNullOrWhiteSpace(trimmedReason) || trimmedReason.Length < MinReasonLength))
        {
            return new ServiceError(422, "retention_reason_required", $"A reason of at least {MinReasonLength} characters is required when extending retention beyond the default");
        }

        retention = Retention.Finite(now.AddDays(requested), requested);
        return null;
    }
}
