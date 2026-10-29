using Files.Application.Notifications;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for scheduled/attempted expiration notifications (Dapper, ADR-0007/0009).</summary>
public interface INotificationEventRepository
{
    /// <summary>Bulk-schedules future due events for all eligible files (idempotent upsert).</summary>
    Task<int> ScheduleAllAsync(Guid policyId, int[] intervalsDays, CancellationToken ct = default);

    /// <summary>Schedules events for a single file after its expiry changed (idempotent upsert).</summary>
    Task<int> ScheduleForFileAsync(Guid fileId, Guid policyId, int[] intervalsDays, CancellationToken ct = default);

    /// <summary>Marks all scheduled/failed events for a file superseded (sent events are never touched).</summary>
    Task<int> SupersedeForFileAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims a batch of due events (<c>scheduled</c>/<c>failed</c>, due now) and moves them
    /// to <c>sending</c>, incrementing the attempt counter. Returns the claimed work items.
    /// </summary>
    Task<IReadOnlyList<NotificationWorkItem>> ClaimDueAsync(
        int batch,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Records a provider-accepted send and a matching delivery attempt row.</summary>
    Task MarkAcceptedAsync(Guid eventId, int attemptNumber, string? providerMessageId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Records a failed send and a matching delivery attempt row. When <paramref name="permanent"/> is
    /// true (or the event has exhausted retries) the event stays <c>failed</c> with no retry;
    /// otherwise it is rescheduled at <paramref name="nextAttemptAt"/>.
    /// </summary>
    Task MarkFailedAsync(
        Guid eventId,
        int attemptNumber,
        bool permanent,
        string lastError,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Reclaims events stuck in <c>sending</c> beyond the lease as <c>failed</c> (<c>sender_timeout</c>).</summary>
    Task<int> ReclaimStuckAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
