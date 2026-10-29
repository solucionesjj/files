using Files.Application.Files;
using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Download-link persistence. S7 only needed the logical-deletion path; S8 adds create/list/revoke,
/// owner-scoped lookups, and the atomic <c>max_downloads</c> reservation (ADR-0005).
/// </summary>
public interface IDownloadLinkRepository
{
    /// <summary>Registers a new link in the ambient unit-of-work transaction.</summary>
    void Add(DownloadLink link);

    /// <summary>Marks all active links for a file as revoked and returns the count.</summary>
    Task<int> RevokeActiveForFileAsync(
        Guid fileId,
        Guid revokedBy,
        DateTimeOffset revokedAt,
        CancellationToken ct = default);

    /// <summary>Loads a link by its token hash (no ownership scope; used by anonymous redemption).</summary>
    Task<DownloadLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct = default);

    /// <summary>Returns the link only when owned by <paramref name="ownerUserId"/>, else <c>null</c>.</summary>
    Task<DownloadLink?> GetOwnedAsync(Guid linkId, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Tracked variant for mutating a link (revoke) in the ambient transaction.</summary>
    Task<DownloadLink?> GetOwnedTrackedAsync(Guid linkId, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Owner-scoped link page for a file, newest first (keyset on <c>created_at</c> + id).</summary>
    Task<IReadOnlyList<DownloadLink>> ListForFileAsync(
        Guid fileId,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default);

    /// <summary>Links created by the caller, newest first, optionally filtered by effective status.</summary>
    Task<IReadOnlyList<DownloadLink>> ListByCreatorAsync(
        Guid creatorUserId,
        LinkStatus? status,
        LinkCursor? cursor,
        int limit,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Counts effectively-active links for a file (link-limit enforcement).</summary>
    Task<int> CountActiveForFileAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Counts effectively-active links created by a user (link-limit enforcement).</summary>
    Task<int> CountActiveByCreatorAsync(Guid creatorUserId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>True when the link is active and unexpired (grant redemption re-check).</summary>
    Task<bool> IsActiveAsync(Guid linkId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Atomically releases expired reservations held by the link and reserves one slot if capacity
    /// remains. Returns <c>true</c> when a slot was reserved (the caller then inserts the grant).
    /// </summary>
    Task<bool> TryReserveAsync(Guid linkId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Marks a link's outstanding grants (issued/redeemed) failed and releases their slots.</summary>
    Task<int> RevokeGrantsAsync(Guid linkId, CancellationToken ct = default);

    /// <summary>Recent download events for a link, newest first (for <c>getLinkStats</c>).</summary>
    Task<IReadOnlyList<DownloadEventRecord>> ListRecentEventsAsync(
        Guid linkId,
        int limit,
        CancellationToken ct = default);

    /// <summary>Total failed key attempts for a link (for <c>getLinkStats</c>).</summary>
    Task<int> CountFailedKeyAttemptsAsync(Guid linkId, CancellationToken ct = default);

    /// <summary>Timestamp of the link's most recent completed download, or <c>null</c>.</summary>
    Task<DateTimeOffset?> GetLastDownloadAtAsync(Guid linkId, CancellationToken ct = default);
}
