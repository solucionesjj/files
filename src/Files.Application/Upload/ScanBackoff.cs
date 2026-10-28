namespace Files.Application.Upload;

/// <summary>
/// Exponential backoff for the scan/commit worker retries (D-35). The terminal 24 h window is
/// enforced separately by the upload cleanup job, so this delay only spaces out individual retries.
/// </summary>
public static class ScanBackoff
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(30);

    /// <summary>Returns the retry delay for a file that has failed <paramref name="attempts"/> times.</summary>
    public static TimeSpan DelayFor(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 6);
        var seconds = Math.Min((long)Base.TotalSeconds * (1L << exponent), (long)Max.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
