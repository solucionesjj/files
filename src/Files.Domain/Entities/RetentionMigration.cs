using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Async bulk retention migration (<c>retention_migrations</c> table, ADR-0006).</summary>
public sealed class RetentionMigration
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public RetentionMigrationStatus Status { get; set; } = RetentionMigrationStatus.Pending;

    public Guid TargetPolicyId { get; set; }

    public string Scope { get; set; } = "{}";

    public string Reason { get; set; } = string.Empty;

    public long TotalFiles { get; set; }

    public long ProcessedFiles { get; set; }

    public long FailedFiles { get; set; }

    public string? LastError { get; set; }

    public Guid RequestedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
