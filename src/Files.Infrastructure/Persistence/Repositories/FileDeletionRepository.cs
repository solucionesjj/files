using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Deletion;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper persistence for physical deletion and tombstone purge. Claims use
/// <c>FOR UPDATE SKIP LOCKED</c>; every mutation is guarded on <c>status</c> and
/// <c>legal_hold=false</c> so a hold placed after the claim can never flip a held file to
/// <c>deleted</c> (FR-DEL-010).
/// </summary>
public sealed class FileDeletionRepository(FilesDbContext db) : IFileDeletionRepository
{
    private const string ClaimDueSql = """
        SELECT id AS FileId, storage_key AS StorageKey, deletion_attempts AS Attempts
          FROM files
         WHERE status = 'pending_deletion'
           AND next_deletion_at <= @Now
           AND legal_hold = false
         ORDER BY next_deletion_at
         LIMIT @Batch
         FOR UPDATE SKIP LOCKED
        """;

    private const string IsLegalHoldSql = """
        SELECT legal_hold FROM files WHERE id = @FileId
        """;

    private const string MarkDeletedSql = """
        UPDATE files
           SET status = 'deleted',
               deleted_at = @Now,
               last_deletion_error = NULL,
               next_deletion_at = NULL,
               version = version + 1
         WHERE id = @FileId
           AND status = 'pending_deletion'
           AND legal_hold = false
        """;

    private const string MarkRetryableSql = """
        UPDATE files
           SET deletion_attempts = deletion_attempts + 1,
               next_deletion_at = @NextDeletionAt,
               last_deletion_error = @Error,
               version = version + 1
         WHERE id = @FileId
           AND status = 'pending_deletion'
           AND legal_hold = false
        """;

    private const string MarkPermanentSql = """
        UPDATE files
           SET last_deletion_error = @Error,
               next_deletion_at = NULL,
               version = version + 1
         WHERE id = @FileId
           AND status = 'pending_deletion'
           AND legal_hold = false
        """;

    private const string ClaimPurgeableSql = """
        SELECT id
          FROM files
         WHERE status = 'deleted'
           AND storage_key IS NOT NULL
           AND deleted_at <= @Cutoff
         ORDER BY deleted_at
         LIMIT @Batch
         FOR UPDATE SKIP LOCKED
        """;

    private const string NullStorageKeySql = """
        UPDATE files
           SET storage_key = NULL,
               version = version + 1
         WHERE id = @FileId
           AND status = 'deleted'
           AND storage_key IS NOT NULL
        """;

    public async Task<IReadOnlyList<DeletionWorkItem>> ClaimDueAsync(int batch, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        var rows = await transaction.Connection!.QueryAsync<DeletionWorkItem>(new CommandDefinition(
            ClaimDueSql, new { Batch = batch, Now = now }, transaction, cancellationToken: ct));
        return rows.ToArray();
    }

    public async Task<bool> IsLegalHoldAsync(Guid fileId, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            IsLegalHoldSql, new { FileId = fileId }, transaction, cancellationToken: ct));
    }

    public async Task<int> MarkDeletedAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkDeletedSql, new { FileId = fileId, Now = now }, transaction, cancellationToken: ct));
    }

    public async Task<int> MarkRetryableFailureAsync(Guid fileId, string errorCode, DateTimeOffset nextDeletionAt, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkRetryableSql, new { FileId = fileId, Error = errorCode, NextDeletionAt = nextDeletionAt }, transaction, cancellationToken: ct));
    }

    public async Task<int> MarkPermanentFailureAsync(Guid fileId, string errorCode, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MarkPermanentSql, new { FileId = fileId, Error = errorCode }, transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> ClaimPurgeableAsync(int batch, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var transaction = Transaction();
        var ids = await transaction.Connection!.QueryAsync<Guid>(new CommandDefinition(
            ClaimPurgeableSql, new { Batch = batch, Cutoff = cutoff }, transaction, cancellationToken: ct));
        return ids.ToArray();
    }

    public async Task<int> NullStorageKeyAsync(Guid fileId, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            NullStorageKeySql, new { FileId = fileId }, transaction, cancellationToken: ct));
    }

    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Deletion writes require an active transaction.");
}
