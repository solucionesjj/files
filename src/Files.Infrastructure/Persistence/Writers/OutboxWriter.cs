using Dapper;
using Files.Application.Abstractions;
using Files.Domain.Events;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>Writes domain events to <c>outbox_messages</c> with Dapper in the current transaction.</summary>
public sealed class OutboxWriter(FilesDbContext db) : IOutboxWriter
{
    private static readonly string Sql = SqlResources.Load("Sql.Outbox.InsertOutboxMessage.sql");

    public async Task WriteAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Outbox writes require an active transaction.");

        var parameters = new
        {
            message.EventType,
            message.AggregateType,
            message.AggregateId,
            message.Payload,
        };

        await transaction.Connection!
            .ExecuteAsync(new CommandDefinition(Sql, parameters, transaction, cancellationToken: ct));
    }
}
