using Files.Application.Deletion;

namespace Files.Application.Abstractions.Repositories;

/// <summary>One pending-deletion file claimed for physical deletion.</summary>
public sealed record DeletionWorkItem(Guid FileId, string StorageKey, int Attempts);

/// <summary>One available file to reconcile (DB → S3 direction).</summary>
public sealed record ReconcileFileRow(Guid FileId, string StorageKey);

/// <summary>
/// Persistence for the reconciliation job (FR-DEL-006): DB→S3 missing-object detection and
/// S3→DB orphan detection. Read-only scans are keyset-paged; the only mutation is the guarded
/// <c>quarantined</c> transition, which fails closed (never restores availability).
/// </summary>
public interface IFileReconciliationRepository
{
    /// <summary>Keyset page of <c>available</c> files ordered by id, starting after <paramref name="afterId"/>.</summary>
    Task<IReadOnlyList<ReconcileFileRow>> ListAvailablePageAsync(int batch, Guid afterId, CancellationToken ct = default);

    /// <summary>Returns the subset of <paramref name="keys"/> that have a file row (for orphan detection).</summary>
    Task<IReadOnlyList<string>> ExistingStorageKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default);

    /// <summary>
    /// Marks an available file quarantined because its object is missing (guarded on
    /// <c>status='available'</c>). Returns the affected row count.
    /// </summary>
    Task<int> QuarantineMissingObjectAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default);
}
