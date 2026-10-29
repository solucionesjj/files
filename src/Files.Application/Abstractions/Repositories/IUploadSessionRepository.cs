using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Persistence for upload sessions. Ownership lookups return <c>null</c> for foreign sessions so
/// callers surface a uniform 404 (no existence oracle, ADR-0012).
/// </summary>
public interface IUploadSessionRepository
{
    void Add(UploadSession session);

    Task<UploadSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant for mutations in the ambient unit-of-work transaction.</summary>
    Task<UploadSession?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<UploadSession?> GetOwnedByUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<UploadSession?> GetOwnedByIntegrationAsync(Guid id, Guid integrationId, CancellationToken ct = default);

    /// <summary>Number of non-terminal, non-expired sessions (the per-host concurrency limit).</summary>
    Task<int> CountOpenAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Open (non-terminal, non-expired) sessions owned by one integration (FR-INT-010).</summary>
    Task<int> CountOpenByIntegrationAsync(Guid integrationId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Open sessions whose upload TTL has passed (for the expiry cleanup job).</summary>
    Task<IReadOnlyList<UploadSession>> ListExpiredOpenAsync(DateTimeOffset now, int limit, CancellationToken ct = default);

    /// <summary>Sessions stuck committing/processing past the stale threshold (for multipart abort).</summary>
    Task<IReadOnlyList<UploadSession>> ListStaleCommittingAsync(
        DateTimeOffset staleBefore,
        int limit,
        CancellationToken ct = default);

    /// <summary>Staging paths owned by non-terminal sessions (protected from orphan cleanup).</summary>
    Task<IReadOnlyList<string>> ListOpenStagingPathsAsync(CancellationToken ct = default);
}
