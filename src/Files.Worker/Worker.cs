using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>
/// S1 placeholder worker. The host already runs OpenTelemetry + structured logging; the Quartz
/// scheduler, advisory locks, and jobs (scan/commit, retention, deletion, notifications) arrive in
/// later milestones.
/// </summary>
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Started(logger);
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Files.Worker started (S1 placeholder; scheduler arrives in a later milestone)")]
        public static partial void Started(ILogger logger);
    }
}
