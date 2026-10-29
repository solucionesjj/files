using Files.Application.Admin;
using Files.Application.Files;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Read access to the <c>download_events</c> table for the admin/auditor read endpoint
/// (FR-ADM-005/008, FR-AUD-001). Read-only: download events are written by the grant/integration
/// download pipelines and never mutated here.
/// </summary>
public interface IDownloadEventRepository
{
    Task<IReadOnlyList<DownloadEventRecord>> ListAsync(DownloadEventQuery query, int limit, CancellationToken ct = default);
}
