namespace Files.Application.Integrations;

/// <summary>Accumulated daily transfer for one scope, read from <c>daily_usage</c>.</summary>
public readonly record struct DailyUsageTotals(long BytesUploaded, long BytesDownloaded);

/// <summary>
/// Pure quota arithmetic for the integration API (FR-INT-010). Kept free of persistence and clock
/// access so the limits are unit-testable. All comparisons are overflow-safe.
/// </summary>
public static class IntegrationQuota
{
    public const long DefaultMaxFileSizeBytes = 5L * 1024 * 1024 * 1024;

    public const int DefaultMaxUploadSessionsPerIntegration = 5;

    public const long DefaultDailyUploadBytes = 50L * 1024 * 1024 * 1024;

    public const long DefaultDailyDownloadBytes = 200L * 1024 * 1024 * 1024;

    public static bool ExceedsMaxFileSize(long sizeBytes, long maxFileSizeBytes) =>
        sizeBytes > maxFileSizeBytes;

    public static bool ExceedsSessionLimit(int openSessions, int maxSessions) =>
        maxSessions >= 0 && openSessions >= maxSessions;

    /// <summary>Whether adding <paramref name="additional"/> to <paramref name="used"/> exceeds the quota.</summary>
    public static bool WouldExceed(long used, long additional, long quota) =>
        additional > 0 && used > quota - additional;
}
