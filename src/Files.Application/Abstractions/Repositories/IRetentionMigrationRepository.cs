using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Async bulk retention migrations (ADR-0006).</summary>
public interface IRetentionMigrationRepository
{
    void Add(RetentionMigration migration);

    Task<RetentionMigration?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Atomically claims the oldest pending migration (pending → running).</summary>
    Task<RetentionMigration?> ClaimNextAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Applies the target policy default to the next page of in-scope files (human, available,
    /// not on legal hold), ordered by id, and returns the ids changed. <paramref name="afterId"/>
    /// is the keyset cursor (pass <see cref="Guid.Empty"/> for the first page).
    /// </summary>
    Task<IReadOnlyList<Guid>> ApplyToNextPageAsync(
        Guid targetPolicyId,
        int targetDays,
        string reason,
        int batchSize,
        Guid afterId,
        CancellationToken ct = default);

    /// <summary>Total number of in-scope files (human, available, not on legal hold).</summary>
    Task<long> CountInScopeAsync(CancellationToken ct = default);

    Task MarkCompletedAsync(Guid id, long total, long processed, long failed, DateTimeOffset now, CancellationToken ct = default);

    Task MarkFailedAsync(Guid id, string failureReason, DateTimeOffset now, CancellationToken ct = default);
}
