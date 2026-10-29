using Files.Application.Configuration;
using Files.Application.Deletion;
using Files.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Reconciliation pass (daily 02:00 UTC, lock <c>files:reconcile</c>): DB↔S3 drift detection (FR-DEL-006).</summary>
public sealed partial class ReconciliationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ReconciliationJob> logger,
    SchedulerOptions scheduler,
    LifecycleMetrics metrics) : BackgroundService
{
    public const string JobName = "files:reconcile";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.ReconcileSeconds));
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
                Log.ReconcileFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
        var summary = await service.RunOnceAsync(ct);
        metrics.RecordJobSuccess(JobName, TimeProvider.System.GetUtcNow());
        Log.Completed(logger, summary.MissingQuarantined, summary.OrphansMoved, summary.StagingDeleted);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Reconciliation pass: {MissingQuarantined} quarantined, {OrphansMoved} orphans moved, {StagingDeleted} staging deleted")]
        public static partial void Completed(ILogger logger, int missingQuarantined, int orphansMoved, int stagingDeleted);

        [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation pass failed")]
        public static partial void ReconcileFailed(ILogger logger, Exception exception);
    }
}
