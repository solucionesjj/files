using Files.Application.Configuration;
using Files.Application.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>Expiration pass (every 15 min): expires past-due files, skipping legal hold (ADR-0006/0008).</summary>
public sealed partial class ExpirationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpirationJob> logger,
    SchedulerOptions scheduler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduler.ExpirationSeconds));
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
                Log.ExpirationFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExpirationService>();
        var summary = await service.RunOnceAsync(ct);
        if (summary.Expired > 0)
        {
            Log.Expired(logger, summary.Expired);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Expiration pass: {Expired} files expired")]
        public static partial void Expired(ILogger logger, int expired);

        [LoggerMessage(Level = LogLevel.Error, Message = "Expiration pass failed")]
        public static partial void ExpirationFailed(ILogger logger, Exception exception);
    }
}
