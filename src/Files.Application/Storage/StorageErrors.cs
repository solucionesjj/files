namespace Files.Application.Storage;

/// <summary>Classification of a storage operation failure (ADR-0008).</summary>
public enum StorageErrorKind
{
    /// <summary>Transient (5xx, timeout, throttling) — safe to retry with backoff.</summary>
    Retryable,

    /// <summary>Non-retryable (access denied, missing bucket) — requires operator intervention.</summary>
    Permanent,
}

/// <summary>
/// A storage failure surfaced by <see cref="IObjectStore"/> implementations without leaking the
/// provider SDK or any object key/filename. The <see cref="Code"/> is a stable, sanitized token
/// suitable for logs, metrics, and <c>last_deletion_error</c>; it never contains a key or secret.
/// </summary>
public sealed class StorageException : Exception
{
    public StorageException(StorageErrorKind kind, string code, string message)
        : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public StorageErrorKind Kind { get; }

    public string Code { get; }
}

/// <summary>Pure translation of provider error codes into sanitized, retryable/permanent classes.</summary>
public static class StorageErrors
{
    /// <summary>Maps a provider error code + HTTP status to a stable, non-leaking token.</summary>
    public static string SanitizeCode(string? providerErrorCode, int? statusCode)
    {
        if (string.Equals(providerErrorCode, "NoSuchBucket", StringComparison.Ordinal))
        {
            return "no_such_bucket";
        }

        if (string.Equals(providerErrorCode, "AccessDenied", StringComparison.Ordinal))
        {
            return "access_denied";
        }

        if (string.Equals(providerErrorCode, "SlowDown", StringComparison.Ordinal)
            || string.Equals(providerErrorCode, "Throttling", StringComparison.Ordinal))
        {
            return "throttled";
        }

        if (string.Equals(providerErrorCode, "RequestTimeout", StringComparison.Ordinal))
        {
            return "timeout";
        }

        if (string.Equals(providerErrorCode, "InternalError", StringComparison.Ordinal))
        {
            return "server_error";
        }

        if (string.Equals(providerErrorCode, "ServiceUnavailable", StringComparison.Ordinal))
        {
            return "service_unavailable";
        }

        if (statusCode >= 500)
        {
            return "server_error";
        }

        if (statusCode == 429)
        {
            return "throttled";
        }

        if (statusCode == 408)
        {
            return "timeout";
        }

        return "s3_error";
    }

    /// <summary>True when the failure is transient and may be retried (5xx, 429, 408, throttling).</summary>
    public static bool IsRetryable(string? providerErrorCode, int? statusCode) =>
        statusCode >= 500
        || statusCode == 429
        || statusCode == 408
        || string.Equals(providerErrorCode, "SlowDown", StringComparison.Ordinal)
        || string.Equals(providerErrorCode, "Throttling", StringComparison.Ordinal)
        || string.Equals(providerErrorCode, "RequestTimeout", StringComparison.Ordinal)
        || string.Equals(providerErrorCode, "InternalError", StringComparison.Ordinal)
        || string.Equals(providerErrorCode, "ServiceUnavailable", StringComparison.Ordinal);
}
