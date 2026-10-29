using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Admin;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Dapper read of the unified audit trail (file + admin events).</summary>
public sealed class AuditLogRepository(FilesDbContext db) : IAuditLogRepository
{
    private static readonly string ListSql = SqlResources.Load("Sql.Audit.ListAuditEvents.sql");

    public async Task<IReadOnlyList<AuditEventRecord>> ListAsync(
        AuditEventQuery query,
        int limit,
        CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var rows = await connection.QueryAsync<AuditEventRow>(
            new CommandDefinition(
                ListSql,
                new
                {
                    Category = query.Category,
                    FileId = query.FileId,
                    ActorType = query.ActorType,
                    Action = query.Action,
                    From = query.From,
                    To = query.To,
                    CursorOccurredAt = query.Cursor?.OccurredAt,
                    CursorId = query.Cursor?.Id,
                    Limit = limit,
                },
                cancellationToken: ct));

        return rows.Select(ToRecord).ToList();
    }

    private static AuditEventRecord ToRecord(AuditEventRow row) => new(
        row.Id,
        row.Category,
        row.FileId,
        row.ActorType,
        row.ActorId,
        row.Action,
        row.Result,
        row.Reason,
        row.Metadata,
        row.TraceId,
        new DateTimeOffset(row.OccurredAt, TimeSpan.Zero));

    private sealed record AuditEventRow(
        Guid Id,
        string Category,
        Guid? FileId,
        string ActorType,
        Guid? ActorId,
        string Action,
        string Result,
        string? Reason,
        string? Metadata,
        string? TraceId,
        DateTime OccurredAt);
}
