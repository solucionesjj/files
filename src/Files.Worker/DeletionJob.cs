using Files.Application.Configuration;
using Files.Application.Deletion;
using Files.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Physical deletion pass (every 5 min, lock <c>files:delete</c>): pending_deletion → deleted (ADR-0008).</summary>
public sealed partial class DeletionJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DeletionJob> logger,
    SchedulerOptions scheduler,
    LifecycleMetrics metrics) : BackgroundService
{
    public const string JobName = "files:delete";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.DeletionSeconds));
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
                Log.DeletionFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDeletionService>();
        var summary = await service.RunOnceAsync(ct);
        metrics.RecordJobSuccess(JobName, TimeProvider.System.GetUtcNow());
        if (summary.Deleted + summary.Retried + summary.FailedPermanent + summary.SkippedLegalHold > 0)
        {
            Log.Completed(logger, summary.Deleted, summary.Retried, summary.FailedPermanent, summary.SkippedLegalHold);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deletion pass: {Deleted} deleted, {Retried} retried, {FailedPermanent} permanent, {SkippedLegalHold} held")]
        public static partial void Completed(ILogger logger, int deleted, int retried, int failedPermanent, int skippedLegalHold);

        [LoggerMessage(Level = LogLevel.Error, Message = "Deletion pass failed")]
        public static partial void DeletionFailed(ILogger logger, Exception exception);
    }
}
