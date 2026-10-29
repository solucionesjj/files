using Files.Application.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Processes pending bulk retention migrations in bounded pages (ADR-0006).</summary>
public sealed partial class RetentionMigrationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionMigrationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
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
                Log.MigrationFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRetentionMigrationService>();
        var summary = await service.RunOnceAsync(ct);
        if (summary.MigrationId is not null)
        {
            Log.Progress(logger, summary.MigrationId.Value, summary.Processed, summary.Completed);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Retention migration {MigrationId}: {Processed} processed (completed: {Completed})")]
        public static partial void Progress(ILogger logger, Guid migrationId, long processed, bool completed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Retention migration pass failed")]
        public static partial void MigrationFailed(ILogger logger, Exception exception);
    }
}
