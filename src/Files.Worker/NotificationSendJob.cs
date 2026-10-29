using Files.Application.Configuration;
using Files.Application.Notifications;
using Files.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Minutely notification send pass (ADR-0009). Claims due events atomically and sends via SMTP.</summary>
public sealed partial class NotificationSendJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationSendJob> logger,
    SchedulerOptions scheduler,
    LifecycleMetrics metrics) : BackgroundService
{
    public const string JobName = "files:notify-send";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.NotificationSendSeconds));
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
                Log.SendFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var summary = await service.RunOnceAsync(ct);
        metrics.RecordJobSuccess(JobName, TimeProvider.System.GetUtcNow());
        Log.Completed(logger, summary.Reclaimed, summary.Sent, summary.Failed, summary.Suppressed);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Notification send pass: {Reclaimed} reclaimed, {Sent} sent, {Failed} failed, {Suppressed} suppressed")]
        public static partial void Completed(ILogger logger, int reclaimed, int sent, int failed, int suppressed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Notification send pass failed")]
        public static partial void SendFailed(ILogger logger, Exception exception);
    }
}
