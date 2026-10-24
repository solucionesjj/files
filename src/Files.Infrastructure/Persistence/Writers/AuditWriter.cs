using System.Data.Common;
using Dapper;
using Files.Application.Abstractions;
using Files.Domain.Entities;
using Files.Domain.Events;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>Appends file- and admin-audit rows with Dapper in the current transaction.</summary>
public sealed class AuditWriter(FilesDbContext db) : IAuditWriter
{
    private static readonly string FileAuditSql = SqlResources.Load("Sql.Audit.InsertFileAuditEvent.sql");
    private static readonly string AdminAuditSql = SqlResources.Load("Sql.Audit.InsertAdminAuditEvent.sql");

    public async Task WriteFileAuditAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var transaction = RequireTransaction();

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
            .ExecuteAsync(new CommandDefinition(FileAuditSql, parameters, transaction, cancellationToken: ct));
    }

    public async Task WriteAdminAuditAsync(AdminAuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var transaction = RequireTransaction();

        var parameters = new
        {
            auditEvent.ActorUserId,
            auditEvent.ActorRole,
            auditEvent.Action,
            auditEvent.TargetType,
            auditEvent.TargetId,
            Result = auditEvent.Result.ToString().ToSnakeCase(),
            auditEvent.Reason,
            auditEvent.Metadata,
            auditEvent.IpHash,
            auditEvent.TraceId,
        };

        await transaction.Connection!
            .ExecuteAsync(new CommandDefinition(AdminAuditSql, parameters, transaction, cancellationToken: ct));
    }

    private DbTransaction RequireTransaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Audit writes require an active transaction.");
}
