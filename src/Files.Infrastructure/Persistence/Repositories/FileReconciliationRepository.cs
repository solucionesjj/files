using System.Data;
using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Deletion;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper persistence for reconciliation. Scans are read-only and keyset-paged (no transaction);
/// the only mutation is the guarded <c>quarantined</c> transition (object missing), which runs in
/// the caller's transaction and fails closed.
/// </summary>
public sealed class FileReconciliationRepository(FilesDbContext db) : IFileReconciliationRepository
{
    private const string ListAvailablePageSql = """
        SELECT id AS FileId, storage_key AS StorageKey
          FROM files
         WHERE status = 'available'
           AND id > @AfterId
         ORDER BY id
         LIMIT @Batch
        """;

    private const string ExistingStorageKeysSql = """
        SELECT storage_key
          FROM files
         WHERE storage_key = ANY(@Keys)
        """;

    private const string QuarantineMissingSql = """
        UPDATE files
           SET status = 'quarantined',
               quarantine_reason = 'object_missing',
               version = version + 1
         WHERE id = @FileId
           AND status = 'available'
        """;

    public async Task<IReadOnlyList<ReconcileFileRow>> ListAvailablePageAsync(int batch, Guid afterId, CancellationToken ct = default)
    {
        var connection = Connection();
        var rows = await connection.QueryAsync<ReconcileFileRow>(new CommandDefinition(
            ListAvailablePageSql, new { Batch = batch, AfterId = afterId }, cancellationToken: ct));
        return rows.ToArray();
    }

    public async Task<IReadOnlyList<string>> ExistingStorageKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var connection = Connection();
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            ExistingStorageKeysSql, new { Keys = keys.ToArray() }, cancellationToken: ct));
        return rows.ToArray();
    }

    public async Task<int> QuarantineMissingObjectAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            QuarantineMissingSql, new { FileId = fileId }, transaction, cancellationToken: ct));
    }

    private DbConnection Connection()
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        return connection;
    }

    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Reconciliation writes require an active transaction.");
}
