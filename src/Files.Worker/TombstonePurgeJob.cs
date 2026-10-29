using Files.Application.Configuration;
using Files.Application.Deletion;
using Files.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Tombstone purge pass (daily, lock <c>files:purge</c>): null storage keys past 60 months (ADR-0008).</summary>
public sealed partial class TombstonePurgeJob(
    IServiceScopeFactory scopeFactory,
    ILogger<TombstonePurgeJob> logger,
    SchedulerOptions scheduler,
    LifecycleMetrics metrics) : BackgroundService
{
    public const string JobName = "files:purge";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.PurgeSeconds));
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
                Log.PurgeFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITombstonePurgeService>();
        var summary = await service.RunOnceAsync(ct);
        metrics.RecordJobSuccess(JobName, TimeProvider.System.GetUtcNow());
        if (summary.Purged > 0)
        {
            Log.Purged(logger, summary.Purged);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Tombstone purge pass: {Purged} storage keys nulled")]
        public static partial void Purged(ILogger logger, int purged);

        [LoggerMessage(Level = LogLevel.Error, Message = "Tombstone purge pass failed")]
        public static partial void PurgeFailed(ILogger logger, Exception exception);
    }
}
