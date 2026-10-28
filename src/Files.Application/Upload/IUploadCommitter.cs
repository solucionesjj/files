using Files.Domain.Entities;

namespace Files.Application.Upload;

/// <summary>
/// Shared commit/finalize transitions for the upload vertical slice (ADR-0003). The API uses
/// <see cref="CommitAsync"/> + <see cref="FinalizeAsync"/> on the scanning-disabled path; the
/// worker uses the full set on the scanning-enabled path. All S3 work is retryable: a commit
/// failure throws and leaves the staged bytes intact for a later attempt.
/// </summary>
public interface IUploadCommitter
{
    /// <summary>
    /// Uploads the staged file to S3 (single <c>PutObject</c> or multipart) and verifies the
    /// object with <c>HeadObject</c>. Throws on failure; the caller records <c>upload_commit_failed</c>
    /// and may retry without re-uploading chunks.
    /// </summary>
    Task CommitAsync(FileEntity file, UploadSession session, CancellationToken ct = default);

    /// <summary>Marks the file <c>available</c>, writes audit/outbox, and removes the staging file.</summary>
    Task FinalizeAsync(FileEntity file, UploadSession session, CancellationToken ct = default);

    /// <summary>
    /// Marks the file <c>quarantined</c> with <c>scan_status=infected</c>, writes audit/outbox, and
    /// removes the staging file. The object is never uploaded to S3.
    /// </summary>
    Task QuarantineAsync(FileEntity file, UploadSession session, CancellationToken ct = default);

    /// <summary>
    /// Records a scan/commit pipeline failure: <c>scan_status=failed</c> (still <c>processing</c>),
    /// increments the retry counter, and schedules the next attempt with backoff. The upload session
    /// is left untouched so the 24 h cleanup window (ADR-0003 §6) still closes from completion.
    /// </summary>
    Task MarkScanFailedAsync(FileEntity file, UploadSession session, CancellationToken ct = default);
}
