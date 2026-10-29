using Files.Application.Files;
using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Persistence for file metadata. Lookups are owner-scoped: a foreign file is returned as
/// <c>null</c> so callers can surface a uniform 404 (no existence oracle).
/// </summary>
public interface IFileRepository
{
    void Add(FileEntity file);

    /// <summary>Returns any file by id (no ownership scope; used by anonymous link redemption).</summary>
    Task<FileEntity?> GetByIdAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Tracked variant for mutating a file in the ambient transaction (internal use cases).</summary>
    Task<FileEntity?> GetByIdTrackedAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Marks a tracked file as modified so the <c>version</c> concurrency token bumps on the next
    /// save (used when an update only changes a related table, e.g. tags, so the ETag still changes).
    /// </summary>
    void MarkUpdated(FileEntity file);

    /// <summary>Returns the file only when owned by <paramref name="ownerUserId"/>, else <c>null</c>.</summary>
    Task<FileEntity?> GetOwnedFileAsync(Guid fileId, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Returns the file only when owned by <paramref name="ownerIntegrationId"/>, else <c>null</c>.</summary>
    Task<FileEntity?> GetOwnedIntegrationFileAsync(
        Guid fileId,
        Guid ownerIntegrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Owner-scoped list/search page. Returns at most <paramref name="limit"/> rows in the requested
    /// order, starting after <paramref name="cursor"/> (keyset on the sort key + id tiebreaker). Only
    /// human-owned files are ever returned — integration files are excluded by the owner predicate.
    /// </summary>
    Task<IReadOnlyList<FileEntity>> ListOwnedAsync(
        Guid ownerUserId,
        FileListQuery query,
        FileListCursor? cursor,
        int limit,
        CancellationToken ct = default);
}
