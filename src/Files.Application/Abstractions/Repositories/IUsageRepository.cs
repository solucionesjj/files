using Files.Application.Admin;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Aggregate storage/file/download usage for the admin/auditor usage endpoint (FR-ADM-006). Pure
/// reads over <c>files</c>, <c>download_events</c>, and <c>integrations</c>; no object content is
/// touched.
/// </summary>
public interface IUsageRepository
{
    Task<UsageStatsResult> GetAsync(CancellationToken ct = default);
}
