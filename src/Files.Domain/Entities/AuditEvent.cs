using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Append-only file action audit record (<c>file_audit_events</c> table).</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? FileId { get; set; }

    public ActorType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    public string Action { get; set; } = string.Empty;

    public AuditResult Result { get; set; } = AuditResult.Success;

    public string? Reason { get; set; }

    public string Metadata { get; set; } = "{}";

    public string? IpHash { get; set; }

    public string? TraceId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
