using System.Data;
using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Download-grant persistence (ADR-0005). Claims and completions use raw SQL so the single-use
/// <c>issued → redeemed</c> transition and the completion counter increment are atomic; the issued
/// grant itself is inserted through EF in the ambient unit-of-work transaction.
/// </summary>
public sealed class DownloadGrantRepository(FilesDbContext db) : IDownloadGrantRepository
{
    private static readonly string ClaimSql = SqlResources.Load("Sql.Download.ClaimGrant.sql");
    private static readonly string LookupSql = SqlResources.Load("Sql.Download.LookupGrant.sql");
    private static readonly string CompleteSql = SqlResources.Load("Sql.Download.CompleteGrant.sql");
    private static readonly string FailSql = SqlResources.Load("Sql.Download.FailGrant.sql");

    public void Add(DownloadGrant grant) => db.DownloadGrants.Add(grant);

    public async Task<GrantClaim?> ClaimAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<ClaimRow>(
            new CommandDefinition(ClaimSql, new { TokenHash = tokenHash, Now = now }, transaction, cancellationToken: ct));

        return row is null
            ? null
            : new GrantClaim(
                row.Id,
                row.FileId,
                row.LinkId,
                Enum.Parse<GrantSource>(row.Source.ToPascalCase(), ignoreCase: false),
                row.IssuedToUserId);
    }

    public async Task<GrantState?> GetStateAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var state = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(LookupSql, new { TokenHash = tokenHash }, cancellationToken: ct));

        return state is null ? null : Enum.Parse<GrantState>(state.ToPascalCase(), ignoreCase: false);
    }

    public async Task CompleteAsync(Guid grantId, Guid fileId, long bytesSent, CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(CompleteSql, new { GrantId = grantId, FileId = fileId, BytesSent = bytesSent }, transaction, cancellationToken: ct));
    }

    public async Task FailAsync(Guid grantId, Guid fileId, string reasonCode, CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(FailSql, new { GrantId = grantId, FileId = fileId, ReasonCode = reasonCode }, transaction, cancellationToken: ct));
    }

    private DbTransaction RequireTransaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Grant writes require an active transaction.");

    private sealed record ClaimRow(Guid Id, Guid FileId, Guid? LinkId, string Source, Guid? IssuedToUserId);
}
