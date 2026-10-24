using Files.Domain.Entities;

namespace Files.Application.Abstractions;

/// <summary>Appends an audit row in the current transaction (append-only, no updates).</summary>
public interface IAuditWriter
{
    Task WriteFileAuditAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
