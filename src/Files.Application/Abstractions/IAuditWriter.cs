using Files.Domain.Entities;
using Files.Domain.Events;

namespace Files.Application.Abstractions;

/// <summary>Appends audit rows in the current transaction (append-only, no updates).</summary>
public interface IAuditWriter
{
    Task WriteFileAuditAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>Appends an administration audit row (<c>admin_audit_events</c>) for admin actions.</summary>
    Task WriteAdminAuditAsync(AdminAuditEvent auditEvent, CancellationToken ct = default);
}
