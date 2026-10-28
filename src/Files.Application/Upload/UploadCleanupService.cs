using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Upload;

/// <summary>
/// Default <see cref="IUploadCleanupService"/> (ADR-0003). Expired open sessions are failed and their
/// staging removed; <c>committing</c> sessions stuck past <c>AbortIncompleteAfterHours</c> have their
/// multipart upload aborted and are failed; any <c>*.part</c> file not owned by an active session is
/// removed. Terminal session staging is never deleted while the scan/commit window is open.
/// </summary>
public sealed partial class UploadCleanupService(
    IUploadSessionRepository sessions,
    IUnitOfWork unitOfWork,
    IChunkStore chunkStore,
    IObjectStore objectStore,
    TimeProvider timeProvider,
    StorageOptions storage,
    ILogger<UploadCleanupService> logger) : IUploadCleanupService
{
    private const int BatchSize = 100;

    public async Task<UploadCleanupSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var expired = await ExpireSessionsAsync(ct);
        var aborted = await AbortStaleMultipartAsync(ct);
        var orphans = await CleanOrphanStagingAsync(ct);

        return new UploadCleanupSummary(expired, aborted, orphans);
    }

    private async Task<int> ExpireSessionsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await sessions.ListExpiredOpenAsync(now, BatchSize, ct);
        var count = 0;

        foreach (var summary in expired)
        {
            await unitOfWork.BeginTransactionAsync(ct);
            var tracked = await sessions.GetByIdTrackedAsync(summary.Id, ct);
            if (tracked is not null && IsOpen(tracked.Status) && tracked.ExpiresAt <= now)
            {
                tracked.Status = UploadSessionStatus.Expired;
                tracked.LastError = "Upload session expired";
                await unitOfWork.SaveChangesAsync(ct);
                await unitOfWork.CommitAsync(ct);
                chunkStore.Delete(tracked.StagingPath);
                count++;
            }
            else
            {
                await unitOfWork.RollbackAsync(ct);
            }
        }

        if (count > 0)
        {
            Log.ExpiredSessions(logger, count);
        }

        return count;
    }

    private async Task<int> AbortStaleMultipartAsync(CancellationToken ct)
    {
        var staleBefore = timeProvider.GetUtcNow().AddHours(-storage.AbortIncompleteAfterHours);
        var stale = await sessions.ListStaleCommittingAsync(staleBefore, BatchSize, ct);
        var count = 0;

        foreach (var summary in stale)
        {
            if (summary.S3UploadId is not null)
            {
                try
                {
                    await objectStore.AbortMultipartAsync(ObjectKey(summary.FileId), summary.S3UploadId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.MultipartAbortFailed(logger, summary.Id, ex);
                }
            }

            await unitOfWork.BeginTransactionAsync(ct);
            var tracked = await sessions.GetByIdTrackedAsync(summary.Id, ct);
            if (tracked is not null && tracked.Status == UploadSessionStatus.Committing && tracked.UpdatedAt < staleBefore)
            {
                tracked.Status = UploadSessionStatus.Failed;
                tracked.S3UploadId = null;
                tracked.LastError = "Upload commit exceeded the retry window";
                await unitOfWork.SaveChangesAsync(ct);
                await unitOfWork.CommitAsync(ct);
                chunkStore.Delete(tracked.StagingPath);
                count++;
            }
            else
            {
                await unitOfWork.RollbackAsync(ct);
            }
        }

        if (count > 0)
        {
            Log.AbortedMultipart(logger, count);
        }

        return count;
    }

    private async Task<int> CleanOrphanStagingAsync(CancellationToken ct)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(storage.StagingPath, "*.part");
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }

        if (files.Length == 0)
        {
            return 0;
        }

        // Files owned by an active (non-terminal) session are retained.
        var retainedPaths = await sessions.ListOpenStagingPathsAsync(ct);
        var retained = retainedPaths.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

        var deleted = 0;
        foreach (var file in files)
        {
            if (!retained.Contains(Path.GetFileName(file)))
            {
                chunkStore.Delete(file);
                deleted++;
            }
        }

        if (deleted > 0)
        {
            Log.DeletedOrphans(logger, deleted);
        }

        return deleted;
    }

    private static bool IsOpen(UploadSessionStatus status) =>
        status is UploadSessionStatus.Initiated or UploadSessionStatus.Uploading
            or UploadSessionStatus.Received or UploadSessionStatus.Committing;

    private static string ObjectKey(Guid fileId) => $"f/{fileId:D}";

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} upload sessions")]
        public static partial void ExpiredSessions(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Aborted {Count} stale multipart uploads")]
        public static partial void AbortedMultipart(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} orphaned staging files")]
        public static partial void DeletedOrphans(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to abort stale multipart upload for session {SessionId}")]
        public static partial void MultipartAbortFailed(ILogger logger, Guid sessionId, Exception exception);
    }
}
