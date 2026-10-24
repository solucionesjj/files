using Files.Domain.Events;

namespace Files.Application.Abstractions;

/// <summary>Writes a domain event to the transactional outbox in the current transaction.</summary>
public interface IOutboxWriter
{
    Task WriteAsync(OutboxMessage message, CancellationToken ct = default);
}
