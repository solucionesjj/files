using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// Fail-fast validation of the configuration the host needs at startup.
/// Only configuration key <em>names</em> are ever reported; values are never logged
/// or included in error messages because they may contain secrets.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// Returns the list of problems with the current configuration. An empty list means the
    /// configuration is valid for the current milestone (S1 foundations).
    /// </summary>
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();

        RequireNonEmpty(configuration, "ConnectionStrings__FilesDb", errors);
        RequireNonEmpty(configuration, "Storage__StagingPath", errors);

        ValidateGuidIfPresent(configuration, "Entra__TenantId", errors);
        ValidateGuidIfPresent(configuration, "Entra__ApiClientId", errors);
        ValidatePositiveIntIfPresent(configuration, "Entra__ClockSkewSeconds", errors);
        ValidateHttpEndpointIfPresent(configuration, "OTEL_EXPORTER_OTLP_ENDPOINT", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__MaxFileSizeBytes", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__ChunkSizeBytes", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__S3PartSizeBytes", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__S3SinglePutThresholdBytes", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__StagingReserveBytes", errors);
        ValidatePositiveIntIfPresent(configuration, "Storage__MaxChunkCount", errors);
        ValidatePositiveIntIfPresent(configuration, "Storage__MaxConcurrentSessions", errors);
        ValidatePositiveIntIfPresent(configuration, "Storage__UploadSessionTtlMinutes", errors);
        ValidatePositiveIntIfPresent(configuration, "Storage__AbortIncompleteAfterHours", errors);
        ValidatePositiveIntIfPresent(configuration, "Storage__GrantTtlSeconds", errors);

        ValidatePositiveIntIfPresent(configuration, "ClamAV__Port", errors);
        ValidatePositiveIntIfPresent(configuration, "ClamAV__ScanTimeoutSeconds", errors);

        ValidatePositiveIntIfPresent(configuration, "Links__PublicTtlDays", errors);
        ValidatePositiveIntIfPresent(configuration, "Links__PrivateTtlDays", errors);
        RequireNonEmpty(configuration, "Auth__LinkKeyPepper", errors);
        ValidatePositiveIntIfPresent(configuration, "Auth__LinkKeyPepperVersion", errors);

        ValidatePositiveIntIfPresent(configuration, "Abuse__MaxKeyAttemptsPerWindow", errors);
        ValidatePositiveIntIfPresent(configuration, "Abuse__KeyAttemptWindowMinutes", errors);
        ValidatePositiveIntIfPresent(configuration, "Abuse__KeyLockoutMinutes", errors);
        ValidatePositiveIntIfPresent(configuration, "Abuse__MaxFailuresPerLinkPerHour", errors);

        ValidatePositiveIntIfPresent(configuration, "Smtp__Port", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__NotificationScheduleSeconds", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__NotificationSendSeconds", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__ExpirationSeconds", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__NotificationMaxAttempts", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__SenderReclaimSeconds", errors);
        ValidatePositiveIntIfPresent(configuration, "Scheduler__MigrationBatchSize", errors);

        return errors;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> listing the offending key names when the
    /// configuration is invalid. Call this before building the host.
    /// </summary>
    public static void ThrowIfInvalid(IConfiguration configuration)
    {
        var errors = Validate(configuration);
        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Invalid or missing required configuration: " + string.Join(", ", errors));
    }

    // Environment variables use "__" as the hierarchy separator (e.g. Storage__StagingPath) and
    // are translated to ":" when loaded into IConfiguration (Storage:StagingPath). We accept the
    // operator-facing "__" form in error messages and translate it for the lookup.
    private static string? GetValue(IConfiguration configuration, string key) =>
        configuration[key.Replace("__", ":", StringComparison.Ordinal)];

    private static void RequireNonEmpty(IConfiguration configuration, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetValue(configuration, key)))
        {
            errors.Add($"{key} is required");
        }
    }

    private static void ValidateGuidIfPresent(IConfiguration configuration, string key, List<string> errors)
    {
        var value = GetValue(configuration, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Guid.TryParse(value, out _))
        {
            errors.Add($"{key} must be a valid GUID");
        }
    }

    private static void ValidateHttpEndpointIfPresent(IConfiguration configuration, string key, List<string> errors)
    {
        var value = GetValue(configuration, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{key} must be an absolute http(s) URL");
        }
    }

    private static void ValidatePositiveLongIfPresent(IConfiguration configuration, string key, List<string> errors)
    {
        var value = GetValue(configuration, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            errors.Add($"{key} must be a positive integer");
        }
    }

    private static void ValidatePositiveIntIfPresent(IConfiguration configuration, string key, List<string> errors)
    {
        var value = GetValue(configuration, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            errors.Add($"{key} must be a positive integer");
        }
    }
}
