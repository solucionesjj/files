using Files.Application.Deletion;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Persistence for the physical deletion job and tombstone purge (ADR-0008). Claims are atomic
/// (<c>FOR UPDATE SKIP LOCKED</c>); state updates are guarded by <c>status</c> + <c>legal_hold</c>
/// so a hold placed after the claim can never result in a physical delete.
/// </summary>
public interface IFileDeletionRepository
{
    /// <summary>
    /// Claims up to <paramref name="batch"/> <c>pending_deletion</c> files that are due
    /// (<c>next_deletion_at &lt;= now</c>) and not on legal hold.
    /// </summary>
    Task<IReadOnlyList<DeletionWorkItem>> ClaimDueAsync(int batch, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>True when the file currently has a legal hold (re-checked right before the S3 delete).</summary>
    Task<bool> IsLegalHoldAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Transitions a file to <c>deleted</c> (guarded; returns the affected row count).</summary>
    Task<int> MarkDeletedAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Records a retryable failure: increments attempts and schedules the next attempt.</summary>
    Task<int> MarkRetryableFailureAsync(Guid fileId, string errorCode, DateTimeOffset nextDeletionAt, CancellationToken ct = default);

    /// <summary>Records a permanent failure (no backoff; the file stays non-downloadable).</summary>
    Task<int> MarkPermanentFailureAsync(Guid fileId, string errorCode, CancellationToken ct = default);

    /// <summary>Claims <c>deleted</c> tombstones past the retention window that still hold a storage key.</summary>
    Task<IReadOnlyList<Guid>> ClaimPurgeableAsync(int batch, DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>Nulls the storage key on a tombstone, retaining all other metadata (audit).</summary>
    Task<int> NullStorageKeyAsync(Guid fileId, CancellationToken ct = default);
}
