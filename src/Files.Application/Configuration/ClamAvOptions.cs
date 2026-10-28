using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// ClamAV daemon client configuration (D-35). The worker scans the staged file with clamd over
/// TCP <c>INSTREAM</c> before the S3 commit; the fail-fast <see cref="ConfigurationValidator"/>
/// rejects malformed values at startup. Scans are serialized one at a time (clamd's default single
/// concurrent scan) to bound memory/CPU.
/// </summary>
public sealed record ClamAvOptions(
    string Host,
    int Port,
    int ScanTimeoutSeconds)
{
    public static ClamAvOptions FromConfiguration(IConfiguration configuration) => new(
        Host: configuration["ClamAV:Host"] ?? "clamav",
        Port: configuration.GetValue("ClamAV:Port", 3310),
        ScanTimeoutSeconds: configuration.GetValue("ClamAV:ScanTimeoutSeconds", 900));
}
