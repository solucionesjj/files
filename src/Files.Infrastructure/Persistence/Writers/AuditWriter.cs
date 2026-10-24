using Dapper;
using Files.Application.Abstractions;
using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>Appends a file-audit row with Dapper in the current transaction.</summary>
public sealed class AuditWriter(FilesDbContext db) : IAuditWriter
{
    private static readonly string Sql = SqlResources.Load("Sql.Audit.InsertFileAuditEvent.sql");

    public async Task WriteFileAuditAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Audit writes require an active transaction.");

        var parameters = new
        {
            auditEvent.FileId,
            ActorType = auditEvent.ActorType.ToString().ToSnakeCase(),
            auditEvent.ActorId,
            auditEvent.Action,
            Result = auditEvent.Result.ToString().ToSnakeCase(),
            auditEvent.Reason,
            auditEvent.Metadata,
            auditEvent.IpHash,
            auditEvent.TraceId,
        };

        await transaction.Connection!
            .ExecuteAsync(new CommandDefinition(Sql, parameters, transaction, cancellationToken: ct));
    }
}
