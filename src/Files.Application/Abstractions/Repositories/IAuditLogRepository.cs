using Files.Application.Admin;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Read access to the append-only audit trail (<c>file_audit_events</c> + <c>admin_audit_events</c>)
/// for the admin/auditor read endpoints (FR-ADM-005/008, FR-AUD-001/002). Read-only: the writers
/// (<see cref="IAuditWriter"/>) are the only mutation path.
/// </summary>
public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditEventRecord>> ListAsync(AuditEventQuery query, int limit, CancellationToken ct = default);
}
