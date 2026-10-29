using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Notifications;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper persistence for <c>notification_events</c> and <c>notification_delivery_attempts</c>.
/// Scheduling is an idempotent upsert on the unique (file_id, interval_days, expiration_at) key;
/// sending claims due rows atomically with <c>FOR UPDATE SKIP LOCKED</c> (ADR-0007/0009).
/// </summary>
public sealed class NotificationEventRepository(FilesDbContext db) : INotificationEventRepository
{
    private const string ScheduleAllSql = """
        INSERT INTO notification_events
            (id, file_id, policy_id, interval_days, expiration_at, recipient_email, status, next_attempt_at)
        SELECT uuidv7(), f.id, @PolicyId, i.days, f.expires_at, u.email, 'scheduled',
               f.expires_at - make_interval(days => i.days)
          FROM files f
          JOIN users u ON u.id = f.owner_user_id
         CROSS JOIN unnest(@Intervals::integer[]) AS i(days)
         WHERE f.status = 'available'
           AND f.owner_type = 'human'
           AND f.retention_mode = 'finite'
           AND f.expires_at IS NOT NULL
           AND f.legal_hold = false
           AND u.email IS NOT NULL
           AND f.expires_at - make_interval(days => i.days) > now()
        ON CONFLICT (file_id, interval_days, expiration_at)
        DO UPDATE SET status = 'scheduled', attempts = 0, last_error = NULL,
                      next_attempt_at = EXCLUDED.next_attempt_at, updated_at = now()
        WHERE notification_events.status = 'superseded'
        """;

    private const string ScheduleForFileSql = """
        INSERT INTO notification_events
            (id, file_id, policy_id, interval_days, expiration_at, recipient_email, status, next_attempt_at)
        SELECT uuidv7(), f.id, @PolicyId, i.days, f.expires_at, u.email, 'scheduled',
               f.expires_at - make_interval(days => i.days)
          FROM files f
          JOIN users u ON u.id = f.owner_user_id
         CROSS JOIN unnest(@Intervals::integer[]) AS i(days)
         WHERE f.id = @FileId
           AND f.status = 'available'
           AND f.retention_mode = 'finite'
           AND f.expires_at IS NOT NULL
           AND f.legal_hold = false
           AND u.email IS NOT NULL
           AND f.expires_at - make_interval(days => i.days) > now()
        ON CONFLICT (file_id, interval_days, expiration_at)
        DO UPDATE SET status = 'scheduled', attempts = 0, last_error = NULL,
                      next_attempt_at = EXCLUDED.next_attempt_at, updated_at = now()
        WHERE notification_events.status = 'superseded'
        """;

    private const string SupersedeForFileSql = """
        UPDATE notification_events
           SET status = 'superseded', updated_at = now()
         WHERE file_id = @FileId
           AND status IN ('scheduled', 'failed')
        """;

    private const string ClaimIdsSql = """
        SELECT id
          FROM notification_events
         WHERE status IN ('scheduled', 'failed')
           AND next_attempt_at <= @Now
           AND attempts < @MaxAttempts
         ORDER BY next_attempt_at
         LIMIT @Batch
         FOR UPDATE SKIP LOCKED
        """;

    private const string MarkSendingSql = """
        UPDATE notification_events
           SET status = 'sending', attempts = attempts + 1, updated_at = now()
         WHERE id = ANY(@Ids)
        """;

    private const string LoadWorkSql = """
        SELECT e.id AS EventId, e.file_id AS FileId, e.recipient_email AS RecipientEmail,
               e.interval_days AS IntervalDays, e.expiration_at AS ExpirationAt,
               f.original_filename AS FileName, COALESCE(u.display_name, '') AS OwnerName,
               e.attempts AS Attempts
          FROM notification_events e
          JOIN files f ON f.id = e.file_id
          LEFT JOIN users u ON u.id = f.owner_user_id
         WHERE e.id = ANY(@Ids)
        """;

    private const string MarkAcceptedSql = """
        UPDATE notification_events
           SET status = 'accepted', sent_at = @Now, provider_message_id = @ProviderMessageId,
               last_error = NULL, next_attempt_at = NULL, updated_at = now()
         WHERE id = @EventId;

        INSERT INTO notification_delivery_attempts
            (notification_event_id, attempt_number, provider, status, provider_message_id)
        VALUES (@EventId, @AttemptNumber, 'smtp', 'accepted', @ProviderMessageId);
        """;

    private const string MarkFailedSql = """
        UPDATE notification_events
           SET status = 'failed', last_error = @LastError,
               next_attempt_at = @NextAttemptAt, updated_at = now()
         WHERE id = @EventId;

        INSERT INTO notification_delivery_attempts
            (notification_event_id, attempt_number, provider, status, error)
        VALUES (@EventId, @AttemptNumber, 'smtp', 'failed', @LastError);
        """;

    private const string ReclaimStuckSql = """
        UPDATE notification_events
           SET status = 'failed', last_error = 'sender_timeout', updated_at = now()
         WHERE status = 'sending'
           AND updated_at < @Cutoff
        """;

    public async Task<int> ScheduleAllAsync(Guid policyId, int[] intervalsDays, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ScheduleAllSql, new { PolicyId = policyId, Intervals = intervalsDays }, transaction, cancellationToken: ct));
    }

    public async Task<int> ScheduleForFileAsync(Guid fileId, Guid policyId, int[] intervalsDays, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ScheduleForFileSql, new { FileId = fileId, PolicyId = policyId, Intervals = intervalsDays }, transaction, cancellationToken: ct));
    }

    public async Task<int> SupersedeForFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SupersedeForFileSql, new { FileId = fileId }, transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<NotificationWorkItem>> ClaimDueAsync(
        int batch,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var transaction = Transaction();

        var ids = (await transaction.Connection!.QueryAsync<Guid>(new CommandDefinition(
            ClaimIdsSql, new { Batch = batch, MaxAttempts = maxAttempts, Now = now }, transaction, cancellationToken: ct)))
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkSendingSql, new { Ids = ids }, transaction, cancellationToken: ct));

        var rows = await transaction.Connection!.QueryAsync<WorkRow>(new CommandDefinition(
            LoadWorkSql, new { Ids = ids }, transaction, cancellationToken: ct));

        return rows
            .Select(r => new NotificationWorkItem(
                r.EventId, r.FileId, r.RecipientEmail, r.IntervalDays,
                new DateTimeOffset(r.ExpirationAt, TimeSpan.Zero), r.FileName, r.OwnerName, r.Attempts))
            .ToArray();
    }

    public async Task MarkAcceptedAsync(Guid eventId, int attemptNumber, string? providerMessageId, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkAcceptedSql, new { EventId = eventId, AttemptNumber = attemptNumber, ProviderMessageId = providerMessageId, Now = now }, transaction, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(
        Guid eventId,
        int attemptNumber,
        bool permanent,
        string lastError,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var transaction = Transaction();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkFailedSql,
            new { EventId = eventId, AttemptNumber = attemptNumber, LastError = lastError, NextAttemptAt = nextAttemptAt },
            transaction,
            cancellationToken: ct));
    }

    public async Task<int> ReclaimStuckAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ReclaimStuckSql, new { Cutoff = cutoff }, transaction, cancellationToken: ct));
    }

    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Notification writes require an active transaction.");

    private sealed record WorkRow(
        Guid EventId,
        Guid FileId,
        string? RecipientEmail,
        int IntervalDays,
        DateTime ExpirationAt,
        string FileName,
        string OwnerName,
        int Attempts);
}
