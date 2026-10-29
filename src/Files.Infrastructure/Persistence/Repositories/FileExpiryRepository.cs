using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Claims past-expiry, non-held files for the expiration job (<c>FOR UPDATE SKIP LOCKED</c>).</summary>
public sealed class FileExpiryRepository(FilesDbContext db) : IFileExpiryRepository
{
    private const string ClaimSql = """
        SELECT id
          FROM files
         WHERE status = 'available'
           AND expires_at IS NOT NULL
           AND expires_at <= @Now
           AND legal_hold = false
         ORDER BY expires_at
         LIMIT @Batch
         FOR UPDATE SKIP LOCKED
        """;

    public async Task<IReadOnlyList<Guid>> ClaimExpiredIdsAsync(int batch, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = Transaction();
        var ids = await transaction.Connection!.QueryAsync<Guid>(new CommandDefinition(
            ClaimSql, new { Batch = batch, Now = now }, transaction, cancellationToken: ct));
        return ids.ToArray();
    }

    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Expiration claims require an active transaction.");
}
