using Files.Domain.Enums;

namespace Files.Domain.Events;

/// <summary>
/// Append-only administration audit row (<c>admin_audit_events</c> table). Written with Dapper in
/// the ambient unit-of-work transaction; the operational audit tables stay Dapper-only.
/// </summary>
public sealed record AdminAuditEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The acting admin's <c>users.id</c> (null only for system-initiated actions).</summary>
    public Guid? ActorUserId { get; init; }

    public string ActorRole { get; init; } = string.Empty;

    public required string Action { get; init; }

    public required string TargetType { get; init; }

    public Guid? TargetId { get; init; }

    public AuditResult Result { get; init; } = AuditResult.Success;

    public string? Reason { get; init; }

    /// <summary>JSON before/after snapshot and non-sensitive context.</summary>
    public string Metadata { get; init; } = "{}";

    public string? IpHash { get; init; }

    public string? TraceId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
