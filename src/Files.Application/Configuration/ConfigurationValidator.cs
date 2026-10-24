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
        ValidateHttpEndpointIfPresent(configuration, "OTEL_EXPORTER_OTLP_ENDPOINT", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__MaxFileSizeBytes", errors);
        ValidatePositiveLongIfPresent(configuration, "Storage__ChunkSizeBytes", errors);

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
}
