using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Upload;

/// <summary>
/// Default <see cref="IScanCommitService"/>. Processes one file at a time (clamd's default single
/// concurrent scan): claim with <c>FOR UPDATE SKIP LOCKED</c>, scan the staged file, then commit,
/// quarantine, or mark the failure for retry. A short lease prevents concurrent workers from
/// double-processing the same file; the commit transaction is never held open around the scan.
/// </summary>
public sealed partial class ScanCommitService(
    IScanWorkClaimer claimer,
    IFileRepository files,
    IUploadSessionRepository sessions,
    IMalwareScanner scanner,
    IUploadCommitter committer,
    ClamAvOptions clamav,
    ILogger<ScanCommitService> logger) : IScanCommitService
{
    private static readonly TimeSpan LeaseGrace = TimeSpan.FromMinutes(5);

    public async Task<ScanCommitSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var lease = TimeSpan.FromSeconds(clamav.ScanTimeoutSeconds) + LeaseGrace;
        var batch = await claimer.ClaimAsync(limit: 1, lease, ct).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return new ScanCommitSummary(0, 0, 0, 0);
        }

        var committed = 0;
        var quarantined = 0;
        var failed = 0;

        foreach (var item in batch)
        {
            switch (await ProcessAsync(item, ct).ConfigureAwait(false))
            {
                case Outcome.Committed:
                    committed++;
                    break;
                case Outcome.Quarantined:
                    quarantined++;
                    break;
                case Outcome.Failed:
                    failed++;
                    break;
            }
        }

        return new ScanCommitSummary(batch.Count, committed, quarantined, failed);
    }

    private async Task<Outcome> ProcessAsync(ScanWorkItem item, CancellationToken ct)
    {
        var file = await files.GetByIdTrackedAsync(item.FileId, ct).ConfigureAwait(false);
        var session = await sessions.GetByIdTrackedAsync(item.SessionId, ct).ConfigureAwait(false);
        if (file is null || session is null
            || session.Status != UploadSessionStatus.Committing
            || file.Status != FileStatus.Processing)
        {
            // Claimed but no longer eligible (terminal transition, expiry, or abort): nothing to do.
            return Outcome.Skipped;
        }

        var result = await scanner.ScanAsync(session.StagingPath, ct).ConfigureAwait(false);
        switch (result)
        {
            case ScanResult.Clean:
                try
                {
                    await committer.CommitAsync(file, session, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.CommitFailed(logger, item.SessionId, ex);
                    await committer.MarkScanFailedAsync(file, session, ct).ConfigureAwait(false);
                    return Outcome.Failed;
                }

                await committer.FinalizeAsync(file, session, ct).ConfigureAwait(false);
                return Outcome.Committed;

            case ScanResult.Infected:
                Log.Infected(logger, item.SessionId);
                await committer.QuarantineAsync(file, session, ct).ConfigureAwait(false);
                return Outcome.Quarantined;

            default:
                await committer.MarkScanFailedAsync(file, session, ct).ConfigureAwait(false);
                return Outcome.Failed;
        }
    }

    private enum Outcome
    {
        Skipped,
        Committed,
        Quarantined,
        Failed,
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "S3 commit failed for upload session {SessionId}; will retry")]
        public static partial void CommitFailed(ILogger logger, Guid sessionId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Infected file quarantined for upload session {SessionId}")]
        public static partial void Infected(ILogger logger, Guid sessionId);
    }
}
