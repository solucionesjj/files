using System.Data;
using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bulk retention migrations: row registration/read via EF, atomic claim + batch apply + progress
/// via Dapper on the ambient transaction (ADR-0006). Applying a page uses a keyset cursor on the
/// time-ordered UUIDv7 <c>id</c>, so resuming/retrying never re-processes an earlier row.
/// </summary>
public sealed class RetentionMigrationRepository(FilesDbContext db) : IRetentionMigrationRepository
{
    private const string ClaimNextSql = """
        WITH claimed AS (
            SELECT id FROM retention_migrations
             WHERE status = 'pending'
             ORDER BY created_at
             LIMIT 1
             FOR UPDATE SKIP LOCKED
        )
        UPDATE retention_migrations m
           SET status = 'running', started_at = @Now, updated_at = now()
          FROM claimed
         WHERE m.id = claimed.id
        RETURNING m.id AS Id, m.target_policy_id AS TargetPolicyId, m.scope AS Scope,
                  m.reason AS Reason, m.requested_by AS RequestedBy, m.created_at AS CreatedAt,
                  m.started_at AS StartedAt;
        """;

    private const string ApplyPageSql = """
        UPDATE files
           SET retention_mode = 'finite',
               expires_at = CASE WHEN (created_at + make_interval(days => @TargetDays)) > now()
                                 THEN (created_at + make_interval(days => @TargetDays))
                                 ELSE now() END,
               retention_days = @TargetDays,
               retention_policy_id = @TargetPolicyId,
               retention_reason = @Reason,
               version = version + 1
         WHERE id IN (
             SELECT id FROM files
              WHERE owner_type = 'human'
                AND status = 'available'
                AND legal_hold = false
                AND id > @AfterId
              ORDER BY id
              LIMIT @BatchSize
         )
        RETURNING id;
        """;

    private const string CountInScopeSql = """
        SELECT count(*)
          FROM files
         WHERE owner_type = 'human'
           AND status = 'available'
           AND legal_hold = false;
        """;

    private const string MarkCompletedSql = """
        UPDATE retention_migrations
           SET status = 'completed', total_files = @Total, processed_files = @Processed,
               failed_files = @Failed, completed_at = @Now, updated_at = now()
         WHERE id = @Id;
        """;

    private const string MarkFailedSql = """
        UPDATE retention_migrations
           SET status = 'failed', last_error = @Error, completed_at = @Now, updated_at = now()
         WHERE id = @Id;
        """;

    public void Add(RetentionMigration migration) => db.RetentionMigrations.Add(migration);

    public Task<RetentionMigration?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.RetentionMigrations.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);

    public async Task<RetentionMigration?> ClaimNextAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<ClaimRow>(new CommandDefinition(
            ClaimNextSql, new { Now = now }, transaction, cancellationToken: ct));

        return row is null
            ? null
            : new RetentionMigration
            {
                Id = row.Id,
                Status = Domain.Enums.RetentionMigrationStatus.Running,
                TargetPolicyId = row.TargetPolicyId,
                Scope = row.Scope,
                Reason = row.Reason,
                RequestedBy = row.RequestedBy,
                CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
                StartedAt = row.StartedAt is null ? null : new DateTimeOffset(row.StartedAt.Value, TimeSpan.Zero),
            };
    }

    public async Task<IReadOnlyList<Guid>> ApplyToNextPageAsync(
        Guid targetPolicyId,
        int targetDays,
        string reason,
        int batchSize,
        Guid afterId,
        CancellationToken ct = default)
    {
        var transaction = Transaction();
        var ids = await transaction.Connection!.QueryAsync<Guid>(new CommandDefinition(
            ApplyPageSql,
            new { TargetPolicyId = targetPolicyId, TargetDays = targetDays, Reason = reason, BatchSize = batchSize, AfterId = afterId },
            transaction,
            cancellationToken: ct));
        return ids.OrderBy(id => id).ToArray();
    }

    public async Task<long> CountInScopeAsync(CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            CountInScopeSql, cancellationToken: ct));
    }

    public async Task MarkCompletedAsync(Guid id, long total, long processed, long failed, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkCompletedSql, new { Id = id, Total = total, Processed = processed, Failed = failed, Now = now }, transaction, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(Guid id, string failureReason, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkFailedSql, new { Id = id, Error = failureReason, Now = now }, transaction, cancellationToken: ct));
    }

    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Retention migration writes require an active transaction.");

    private sealed record ClaimRow(
        Guid Id,
        Guid TargetPolicyId,
        string Scope,
        string Reason,
        Guid RequestedBy,
        DateTime CreatedAt,
        DateTime? StartedAt);
}
