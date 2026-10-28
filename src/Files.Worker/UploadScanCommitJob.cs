using Files.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>
/// Runs the scan/commit worker on a fixed interval (ADR-0003, D-35): claims files awaiting a
/// ClamAV scan, commits clean files to S3, quarantines infected files, and retries failures with
/// backoff. One file is processed per pass (clamd's default single concurrent scan).
/// </summary>
public sealed partial class UploadScanCommitJob(
    IServiceScopeFactory scopeFactory,
    ILogger<UploadScanCommitJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

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
                Log.PassFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var scanCommit = scope.ServiceProvider.GetRequiredService<IScanCommitService>();
        var summary = await scanCommit.RunOnceAsync(ct);
        Log.PassCompleted(logger, summary.Processed, summary.Committed, summary.Quarantined, summary.Failed);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Scan/commit pass completed: {Processed} processed, {Committed} committed, {Quarantined} quarantined, {Failed} failed")]
        public static partial void PassCompleted(ILogger logger, int processed, int committed, int quarantined, int failed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Scan/commit pass failed")]
        public static partial void PassFailed(ILogger logger, Exception exception);
    }
}
