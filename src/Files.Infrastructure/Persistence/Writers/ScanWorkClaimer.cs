using Dapper;
using Files.Application.Abstractions;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>
/// Dapper claim for the scan/commit worker. The select takes row locks with
/// <c>FOR UPDATE SKIP LOCKED</c> and the lease update runs in the same transaction, so a claimed
/// file is never scanned by two workers at once. The transaction commits before any scan begins
/// (no long transaction around the external ClamAV/S3 calls).
/// </summary>
public sealed class ScanWorkClaimer(FilesDbContext db) : IScanWorkClaimer
{
    private static readonly string SelectSql = SqlResources.Load("Sql.Upload.SelectScanWork.sql");
    private static readonly string LeaseSql = SqlResources.Load("Sql.Upload.LeaseScanWork.sql");

    public async Task<IReadOnlyList<ScanWorkItem>> ClaimAsync(int limit, TimeSpan lease, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var dbTransaction = transaction.GetDbTransaction();

        var claimed = (await dbTransaction.Connection!
            .QueryAsync<ScanWorkItem>(
                new CommandDefinition(SelectSql, new { Limit = limit }, dbTransaction, cancellationToken: ct))
            .ConfigureAwait(false))
            .AsList();

        if (claimed.Count > 0)
        {
            await dbTransaction.Connection!
                .ExecuteAsync(new CommandDefinition(
                    LeaseSql,
                    new { LeaseSeconds = (int)lease.TotalSeconds, FileIds = claimed.Select(c => c.FileId).ToArray() },
                    dbTransaction,
                    cancellationToken: ct))
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return claimed;
    }
}
