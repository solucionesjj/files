using Files.Application.Integrations;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Daily transfer accounting (<c>daily_usage</c>, FR-INT-010). Uploads and downloads are recorded
/// against the integration's UTC date; the quota service reads the totals back before admitting new
/// work.
/// </summary>
public interface IDailyUsageRepository
{
    Task<DailyUsageTotals> GetAsync(Guid integrationId, DateOnly usageDate, CancellationToken ct = default);

    Task AddUploadedAsync(Guid integrationId, long bytes, DateOnly usageDate, CancellationToken ct = default);

    Task AddDownloadedAsync(Guid integrationId, long bytes, DateOnly usageDate, CancellationToken ct = default);
}
