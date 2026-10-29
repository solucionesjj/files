namespace Files.Domain.Enums;

/// <summary>Lifecycle of a bulk retention migration (column <c>retention_migrations.status</c>).</summary>
public enum RetentionMigrationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}
