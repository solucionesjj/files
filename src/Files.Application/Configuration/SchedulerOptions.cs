using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// Worker job cadence and notification retry limits (ADR-0007/0009). All intervals are positive
/// seconds; the fail-fast <see cref="ConfigurationValidator"/> rejects malformed values.
/// </summary>
public sealed record SchedulerOptions(
    int NotificationScheduleSeconds,
    int NotificationSendSeconds,
    int ExpirationSeconds,
    int NotificationMaxAttempts,
    int SenderReclaimSeconds,
    int MigrationBatchSize)
{
    public const int DefaultNotificationScheduleSeconds = 3600;
    public const int DefaultNotificationSendSeconds = 60;
    public const int DefaultExpirationSeconds = 900;
    public const int DefaultNotificationMaxAttempts = 5;
    public const int DefaultSenderReclaimSeconds = 600;
    public const int DefaultMigrationBatchSize = 500;

    public static SchedulerOptions FromConfiguration(IConfiguration configuration) => new(
        NotificationScheduleSeconds: configuration.GetValue("Scheduler:NotificationScheduleSeconds", DefaultNotificationScheduleSeconds),
        NotificationSendSeconds: configuration.GetValue("Scheduler:NotificationSendSeconds", DefaultNotificationSendSeconds),
        ExpirationSeconds: configuration.GetValue("Scheduler:ExpirationSeconds", DefaultExpirationSeconds),
        NotificationMaxAttempts: configuration.GetValue("Scheduler:NotificationMaxAttempts", DefaultNotificationMaxAttempts),
        SenderReclaimSeconds: configuration.GetValue("Scheduler:SenderReclaimSeconds", DefaultSenderReclaimSeconds),
        MigrationBatchSize: configuration.GetValue("Scheduler:MigrationBatchSize", DefaultMigrationBatchSize));
}
