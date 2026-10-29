using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Integrations;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Daily transfer accounting (<c>daily_usage</c>, FR-INT-010) via Dapper upserts.</summary>
public sealed class DailyUsageRepository(FilesDbContext db) : IDailyUsageRepository
{
    private static readonly string GetSql =
        "SELECT COALESCE(bytes_uploaded,0) AS BytesUploaded, COALESCE(bytes_downloaded,0) AS BytesDownloaded FROM daily_usage WHERE scope_type = 'integration' AND scope_id = @ScopeId AND usage_date = @Date";

    private static readonly string AddSql = SqlResources.Load("Sql.Usage.AddBytes.sql");

    public async Task<DailyUsageTotals> GetAsync(Guid integrationId, DateOnly usageDate, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<UsageRow>(
            new CommandDefinition(GetSql, new { ScopeId = integrationId, Date = usageDate }, cancellationToken: ct));
        return row is null ? new DailyUsageTotals(0, 0) : new DailyUsageTotals(row.BytesUploaded, row.BytesDownloaded);
    }

    public async Task AddUploadedAsync(Guid integrationId, long bytes, DateOnly usageDate, CancellationToken ct = default) =>
        await AddAsync(integrationId, usageDate, bytes, 0, ct);

    public async Task AddDownloadedAsync(Guid integrationId, long bytes, DateOnly usageDate, CancellationToken ct = default) =>
        await AddAsync(integrationId, usageDate, 0, bytes, ct);

    private async Task AddAsync(Guid integrationId, DateOnly date, long uploadedBytes, long downloadedBytes, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        var parameters = new { ScopeId = integrationId, Date = date, UploadedBytes = uploadedBytes, DownloadedBytes = downloadedBytes };
        if (transaction is not null)
        {
            await transaction.Connection!.ExecuteAsync(
                new CommandDefinition(AddSql, parameters, transaction, cancellationToken: ct));
            return;
        }

        var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(AddSql, parameters, cancellationToken: ct));
    }

    private async Task<IDbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection;
    }

    private sealed record UsageRow(long BytesUploaded, long BytesDownloaded);
}
