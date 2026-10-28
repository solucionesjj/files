using Files.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Files.Worker;

/// <summary>
/// Runs upload maintenance on a fixed interval (ADR-0003 §6): expires sessions past their TTL,
/// aborts stale multipart uploads, and removes orphaned staging files.
/// </summary>
public sealed partial class UploadCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<UploadCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

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
                Log.CleanupFailed(logger, ex);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IUploadCleanupService>();
        var summary = await cleanup.RunOnceAsync(ct);
        Log.CleanupCompleted(logger, summary.ExpiredSessions, summary.AbortedMultipart, summary.DeletedOrphans);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Upload cleanup completed: {ExpiredSessions} expired, {AbortedMultipart} aborted, {DeletedOrphans} orphans deleted")]
        public static partial void CleanupCompleted(ILogger logger, int expiredSessions, int abortedMultipart, int deletedOrphans);

        [LoggerMessage(Level = LogLevel.Error, Message = "Upload cleanup pass failed")]
        public static partial void CleanupFailed(ILogger logger, Exception exception);
    }
}
