namespace Files.Application.Upload;

/// <summary>Outcome of one scan/commit worker pass.</summary>
public sealed record ScanCommitSummary(int Processed, int Committed, int Quarantined, int Failed);

/// <summary>
/// The scan/commit worker use case (ADR-0003, D-35): claims files in
/// <c>processing</c> + <c>scan_status IN ('pending','failed')</c>, scans them with ClamAV, and then
/// commits to S3 (clean), quarantines them (infected), or schedules a retry (scanner/S3 failure).
/// </summary>
public interface IScanCommitService
{
    Task<ScanCommitSummary> RunOnceAsync(CancellationToken ct = default);
}
