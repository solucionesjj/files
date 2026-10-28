using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// Upload/staging limits and geometry, read from the <c>Storage__*</c> keys (ADR-0003). All limits
/// are positive; the fail-fast <see cref="ConfigurationValidator"/> rejects malformed values at
/// startup.
/// </summary>
public sealed record StorageOptions(
    long MaxFileSizeBytes,
    long ChunkSizeBytes,
    int MaxChunkCount,
    long S3PartSizeBytes,
    long S3SinglePutThresholdBytes,
    string StagingPath,
    long StagingReserveBytes,
    int MaxConcurrentSessions,
    int UploadSessionTtlMinutes,
    int AbortIncompleteAfterHours,
    int GrantTtlSeconds,
    bool MalwareScanning)
{
    public static StorageOptions FromConfiguration(IConfiguration configuration) => new(
        MaxFileSizeBytes: configuration.GetValue("Storage:MaxFileSizeBytes", 5L * 1024 * 1024 * 1024),
        ChunkSizeBytes: configuration.GetValue("Storage:ChunkSizeBytes", 16L * 1024 * 1024),
        MaxChunkCount: configuration.GetValue("Storage:MaxChunkCount", 10_000),
        S3PartSizeBytes: configuration.GetValue("Storage:S3PartSizeBytes", 64L * 1024 * 1024),
        S3SinglePutThresholdBytes: configuration.GetValue("Storage:S3SinglePutThresholdBytes", 64L * 1024 * 1024),
        StagingPath: configuration["Storage:StagingPath"] ?? "/var/lib/files/staging",
        StagingReserveBytes: configuration.GetValue("Storage:StagingReserveBytes", 10L * 1024 * 1024 * 1024),
        MaxConcurrentSessions: configuration.GetValue("Storage:MaxConcurrentSessions", 3),
        UploadSessionTtlMinutes: configuration.GetValue("Storage:UploadSessionTtlMinutes", 60),
        AbortIncompleteAfterHours: configuration.GetValue("Storage:AbortIncompleteAfterHours", 24),
        GrantTtlSeconds: configuration.GetValue("Storage:GrantTtlSeconds", 120),
        MalwareScanning: configuration.GetValue("Features:MalwareScanning", false));
}
