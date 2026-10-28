namespace Files.Application.Upload;

/// <summary>Summary of one cleanup pass (ADR-0003 §6).</summary>
public sealed record UploadCleanupSummary(int ExpiredSessions, int AbortedMultipart, int DeletedOrphans);

/// <summary>
/// Background maintenance for uploads: expire sessions past their TTL, abort stale multipart
/// uploads, and remove orphaned staging files. Runs in the worker on a fixed interval.
/// </summary>
public interface IUploadCleanupService
{
    Task<UploadCleanupSummary> RunOnceAsync(CancellationToken ct = default);
}
