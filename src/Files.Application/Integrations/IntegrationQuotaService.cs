using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;

namespace Files.Application.Integrations;

/// <summary>
/// Per-integration quotas (FR-INT-010): max file size, concurrent open upload sessions, and daily
/// uploaded/downloaded bytes. Administrative overrides are read from <c>platform_settings</c>;
/// everything else falls back to the canonical defaults. Upload/download bytes are recorded only
/// after the work actually completes.
/// </summary>
public interface IIntegrationQuotaService
{
    Task<ServiceError?> CheckCreateSessionAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default);

    Task<ServiceError?> CheckDownloadAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default);

    Task RecordUploadedAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default);

    Task RecordDownloadedAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default);
}

public sealed class IntegrationQuotaService(
    IPlatformSettingsRepository settings,
    IDailyUsageRepository dailyUsage,
    IUploadSessionRepository sessions,
    TimeProvider timeProvider,
    StorageOptions storage) : IIntegrationQuotaService
{
    public async Task<ServiceError?> CheckCreateSessionAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default)
    {
        var maxFileSize = await settings.GetLongAsync("max_file_size_bytes", ct) ?? storage.MaxFileSizeBytes;
        if (IntegrationQuota.ExceedsMaxFileSize(sizeBytes, maxFileSize))
        {
            return new ServiceError(413, "file_too_large", "File size exceeds the maximum");
        }

        var maxSessions = await settings.GetIntAsync("max_upload_sessions_per_integration", ct)
            ?? IntegrationQuota.DefaultMaxUploadSessionsPerIntegration;
        var open = await sessions.CountOpenByIntegrationAsync(integrationId, timeProvider.GetUtcNow(), ct);
        if (IntegrationQuota.ExceedsSessionLimit(open, maxSessions))
        {
            return new ServiceError(429, "quota_exceeded", "Maximum concurrent upload sessions reached");
        }

        var dailyUpload = await settings.GetLongAsync("daily_upload_quota_bytes", ct)
            ?? IntegrationQuota.DefaultDailyUploadBytes;
        var today = Today();
        var usage = await dailyUsage.GetAsync(integrationId, today, ct);
        if (IntegrationQuota.WouldExceed(usage.BytesUploaded, sizeBytes, dailyUpload))
        {
            return new ServiceError(429, "quota_exceeded", "Daily upload quota exceeded");
        }

        return null;
    }

    public async Task<ServiceError?> CheckDownloadAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default)
    {
        var dailyDownload = await settings.GetLongAsync("daily_download_quota_bytes", ct)
            ?? IntegrationQuota.DefaultDailyDownloadBytes;
        var usage = await dailyUsage.GetAsync(integrationId, Today(), ct);
        return IntegrationQuota.WouldExceed(usage.BytesDownloaded, sizeBytes, dailyDownload)
            ? new ServiceError(429, "quota_exceeded", "Daily download quota exceeded")
            : null;
    }

    public Task RecordUploadedAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default) =>
        dailyUsage.AddUploadedAsync(integrationId, sizeBytes, Today(), ct);

    public Task RecordDownloadedAsync(Guid integrationId, long sizeBytes, CancellationToken ct = default) =>
        dailyUsage.AddDownloadedAsync(integrationId, sizeBytes, Today(), ct);

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().Date);
}
