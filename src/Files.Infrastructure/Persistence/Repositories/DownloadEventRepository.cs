using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Admin;
using Files.Application.Files;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Dapper read of <c>download_events</c> for the admin/auditor endpoint.</summary>
public sealed class DownloadEventRepository(FilesDbContext db) : IDownloadEventRepository
{
    private static readonly string ListSql = SqlResources.Load("Sql.Audit.ListDownloadEvents.sql");

    public async Task<IReadOnlyList<DownloadEventRecord>> ListAsync(
        DownloadEventQuery query,
        int limit,
        CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var rows = await connection.QueryAsync<DownloadEventRow>(
            new CommandDefinition(
                ListSql,
                new
                {
                    FileId = query.FileId,
                    LinkId = query.LinkId,
                    Result = query.Result,
                    From = query.From,
                    To = query.To,
                    CursorOccurredAt = query.Cursor?.OccurredAt,
                    CursorId = query.Cursor?.Id,
                    Limit = limit,
                },
                cancellationToken: ct));

        return rows.Select(ToRecord).ToList();
    }

    private static DownloadEventRecord ToRecord(DownloadEventRow row) => new(
        row.Id,
        row.FileId,
        row.LinkId,
        row.ActorType,
        row.ActorId,
        row.Source,
        row.Result,
        row.ReasonCode,
        row.BytesSent,
        row.TraceId,
        new DateTimeOffset(row.OccurredAt, TimeSpan.Zero));

    private sealed record DownloadEventRow(
        Guid Id,
        Guid? FileId,
        Guid? LinkId,
        string ActorType,
        Guid? ActorId,
        string Source,
        string Result,
        string? ReasonCode,
        long BytesSent,
        string? TraceId,
        DateTime OccurredAt);
}
