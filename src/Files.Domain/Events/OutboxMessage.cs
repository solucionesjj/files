namespace Files.Domain.Events;

/// <summary>
/// Envelope for a domain event written to the transactional outbox (<c>outbox_messages</c>).
/// <see cref="Payload"/> holds the serialized event body (JSON).
/// </summary>
public sealed record OutboxMessage
{
    public required string EventType { get; init; }

    public required string AggregateType { get; init; }

    public required Guid AggregateId { get; init; }

    public required string Payload { get; init; }
}
