using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Application.Telemetry;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Deletion;

/// <summary>One reconciliation pass summary (FR-DEL-006).</summary>
public sealed record ReconciliationSummary(int MissingQuarantined, int OrphansMoved, int StagingDeleted);

/// <summary>Detects DB/S3 drift and repairs it fail-closed (never restores availability).</summary>
public interface IReconciliationService
{
    Task<ReconciliationSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// DB→S3: <c>available</c> files whose object is missing become <c>quarantined</c>
/// (<c>object_missing</c>) with links revoked and an audit/alert. S3→DB: <c>f/</c> objects with no
/// file row move to <c>orphan/</c> (copy+delete) and <c>staging/</c> objects with no open session
/// are deleted. Object keys are only ever logged by fingerprint.
/// </summary>
public sealed partial class ReconciliationService(
    IFileReconciliationRepository reconciliation,
    IObjectStore objectStore,
    IDownloadLinkRepository links,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    LifecycleMetrics metrics,
    ILogger<ReconciliationService> logger) : IReconciliationService
{
    public async Task<ReconciliationSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var missing = await ReconcileDbToS3Async(now, ct);
        var (orphansMoved, stagingDeleted) = await ReconcileS3ToDbAsync(ct);

        metrics.RecordReconcileMissingDb(missing.Count);
        metrics.RecordReconcileQuarantined(missing.Quarantined);
        metrics.RecordReconcileOrphanS3(orphansMoved + stagingDeleted);

        return new ReconciliationSummary(missing.Quarantined, orphansMoved, stagingDeleted);
    }

    private async Task<(int Count, int Quarantined)> ReconcileDbToS3Async(DateTimeOffset now, CancellationToken ct)
    {
        var missing = 0;
        var quarantined = 0;
        var afterId = Guid.Empty;

        while (true)
        {
            var page = await reconciliation.ListAvailablePageAsync(scheduler.ReconcileBatchSize, afterId, ct);
            foreach (var row in page)
            {
                ObjectHead head;
                try
                {
                    head = await objectStore.HeadObjectAsync(row.StorageKey, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A failed HEAD is inconclusive; never quarantine on it (fail closed).
                    Log.HeadFailed(logger, row.FileId, ex);
                    continue;
                }

                if (head.Exists)
                {
                    continue;
                }

                missing++;
                if (await QuarantineMissingAsync(row.FileId, now, ct))
                {
                    quarantined++;
                    Log.Missing(logger, row.FileId);
                }
            }

            if (page.Count < scheduler.ReconcileBatchSize)
            {
                break;
            }

            afterId = page[^1].FileId;
        }

        return (missing, quarantined);
    }

    private async Task<(int OrphansMoved, int StagingDeleted)> ReconcileS3ToDbAsync(CancellationToken ct)
    {
        var orphansMoved = 0;
        var stagingDeleted = 0;

        var fileObjects = await objectStore.ListObjectsAsync(
            ReconciliationScope.FilePrefix, scheduler.ReconcileBatchSize, ct);

        var fileKeys = fileObjects
            .Where(o => ReconciliationScope.IsFileObject(o.Key))
            .Select(o => o.Key)
            .ToArray();

        if (fileKeys.Length > 0)
        {
            var existing = (await reconciliation.ExistingStorageKeysAsync(fileKeys, ct))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in fileKeys)
            {
                if (existing.Contains(key))
                {
                    continue;
                }

                if (await MoveOrphanAsync(key, ct))
                {
                    orphansMoved++;
                }
            }
        }

        // In the current design staging is local disk (ADR-0003), so any `staging/` S3 object is
        // orphaned by construction and safe to delete (there is no open session it could belong to).
        var stagingObjects = await objectStore.ListObjectsAsync(
            ReconciliationScope.StagingPrefix, scheduler.ReconcileBatchSize, ct);

        foreach (var obj in stagingObjects.Where(o => ReconciliationScope.IsStagingObject(o.Key)))
        {
            if (await DeleteOrphanAsync(obj.Key, ct))
            {
                stagingDeleted++;
            }
        }

        if (orphansMoved + stagingDeleted > 0)
        {
            Log.Orphans(logger, orphansMoved, stagingDeleted);
        }

        return (orphansMoved, stagingDeleted);
    }

    private async Task<bool> MoveOrphanAsync(string key, CancellationToken ct)
    {
        var destination = ReconciliationScope.OrphanDestinationKey(key);
        try
        {
            await objectStore.CopyObjectAsync(key, destination, ct);
            await objectStore.DeleteObjectAsync(key, ct);
            Log.MovedOrphan(logger, ReconciliationScope.Fingerprint(key));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.MoveFailed(logger, ReconciliationScope.Fingerprint(key), ex);
            return false;
        }
    }

    private async Task<bool> DeleteOrphanAsync(string key, CancellationToken ct)
    {
        try
        {
            await objectStore.DeleteObjectAsync(key, ct);
            Log.DeletedStaging(logger, ReconciliationScope.Fingerprint(key));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.StagingDeleteFailed(logger, ReconciliationScope.Fingerprint(key), ex);
            return false;
        }
    }

    private async Task<bool> QuarantineMissingAsync(Guid fileId, DateTimeOffset now, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var affected = await reconciliation.QuarantineMissingObjectAsync(fileId, now, ct);
            if (affected == 0)
            {
                await unitOfWork.RollbackAsync(ct);
                return false;
            }

            await links.RevokeActiveForFileBySystemAsync(fileId, now, "object_missing", ct);

            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = fileId,
                ActorType = ActorType.System,
                Action = "quarantined",
                Result = AuditResult.Success,
                Reason = "object_missing",
                Metadata = JsonSerializer.Serialize(new { priorStatus = "available", quarantineReason = "object_missing" }),
            }, ct);

            await unitOfWork.CommitAsync(ct);
            return true;
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation quarantined file {FileId}: object missing")]
        public static partial void Missing(ILogger logger, Guid fileId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "HEAD of file {FileId} failed; left available (fail closed)")]
        public static partial void HeadFailed(ILogger logger, Guid fileId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciliation moved {OrphansMoved} orphan(s) and deleted {StagingDeleted} staging object(s)")]
        public static partial void Orphans(ILogger logger, int orphansMoved, int stagingDeleted);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Moved orphan object {Fingerprint} to orphan/")]
        public static partial void MovedOrphan(ILogger logger, string fingerprint);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to move orphan object {Fingerprint}")]
        public static partial void MoveFailed(ILogger logger, string fingerprint, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Deleted orphan staging object {Fingerprint}")]
        public static partial void DeletedStaging(ILogger logger, string fingerprint);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete orphan staging object {Fingerprint}")]
        public static partial void StagingDeleteFailed(ILogger logger, string fingerprint, Exception exception);
    }
}
