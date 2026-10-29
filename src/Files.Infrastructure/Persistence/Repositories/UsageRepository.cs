using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Admin;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Dapper aggregate usage read (FR-ADM-006) over files/download_events/integrations.</summary>
public sealed class UsageRepository(FilesDbContext db) : IUsageRepository
{
    private static readonly string Sql = SqlResources.Load("Sql.Usage.UsageStats.sql");

    public async Task<UsageStatsResult> GetAsync(CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, cancellationToken: ct));

        var storage = await multi.ReadSingleAsync<StorageRow>();
        var files = await multi.ReadSingleAsync<FilesRow>();
        var downloads = await multi.ReadSingleAsync<DownloadsRow>();
        var integrations = (await multi.ReadAsync<IntegrationUsageRow>()).ToList();

        return new UsageStatsResult(
            storage.StorageTotalBytes,
            storage.StorageHumanBytes,
            storage.StorageIntegrationBytes,
            files.FilesTotal,
            files.FilesAvailable,
            files.FilesExpired,
            files.FilesPendingDeletion,
            downloads.DownloadsLast30Days,
            downloads.DownloadsLast7Days,
            integrations.Select(i => new IntegrationUsage(i.IntegrationId, i.Name, i.Files, i.Bytes)).ToList());
    }

    private sealed record StorageRow(long StorageTotalBytes, long StorageHumanBytes, long StorageIntegrationBytes);

    private sealed record FilesRow(long FilesTotal, long FilesAvailable, long FilesExpired, long FilesPendingDeletion);

    private sealed record DownloadsRow(long DownloadsLast30Days, long DownloadsLast7Days);

    private sealed record IntegrationUsageRow(Guid IntegrationId, string Name, long Files, long Bytes);
}
