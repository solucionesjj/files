using Files.Application.Configuration;
using Files.Application.Notifications;
using Files.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Hourly notification scheduling pass (ADR-0006/0007). Idempotent upsert, safe to run repeatedly.</summary>
public sealed partial class NotificationScheduleJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationScheduleJob> logger,
    SchedulerOptions scheduler,
    LifecycleMetrics metrics) : BackgroundService
{
    public const string JobName = "files:notify-schedule";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.NotificationScheduleSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.ScheduleFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationScheduler>();
        var scheduled = await service.RunOnceAsync(ct);
        metrics.RecordJobSuccess(JobName, TimeProvider.System.GetUtcNow());
        if (scheduled > 0)
        {
            Log.Scheduled(logger, scheduled);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Notification scheduling pass: {Scheduled} events scheduled/revived")]
        public static partial void Scheduled(ILogger logger, int scheduled);

        [LoggerMessage(Level = LogLevel.Error, Message = "Notification scheduling pass failed")]
        public static partial void ScheduleFailed(ILogger logger, Exception exception);
    }
}
