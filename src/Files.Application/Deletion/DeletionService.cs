using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Application.Telemetry;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Files.Application.Deletion;

/// <summary>One physical-deletion pass summary (FR-DEL-002..005/007/010).</summary>
public sealed record DeletionSummary(int Deleted, int Retried, int FailedPermanent, int SkippedLegalHold);

/// <summary>Physically deletes <c>pending_deletion</c> files and applies retry/backoff (ADR-0008).</summary>
public interface IDeletionService
{
    Task<DeletionSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Claims due <c>pending_deletion</c> files (skip legal hold), calls S3 <c>DeleteObjectAsync</c>
/// (idempotent: missing = success), and either marks the file <c>deleted</c> or records a
/// retryable/permanent failure with backoff. Email delivery is never involved (FR-NOT-010).
/// </summary>
public sealed partial class DeletionService(
    IFileDeletionRepository deletions,
    IObjectStore objectStore,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    LifecycleMetrics metrics,
    ILogger<DeletionService> logger) : IDeletionService
{
    private const string FileAggregateType = "file";

    public async Task<DeletionSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        IReadOnlyList<DeletionWorkItem> workItems;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            workItems = await deletions.ClaimDueAsync(scheduler.DeletionBatchSize, now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        var deleted = 0;
        var retried = 0;
        var failedPermanent = 0;
        var skippedLegalHold = 0;

        foreach (var item in workItems)
        {
            if (await IsHeldAsync(item.FileId, ct))
            {
                skippedLegalHold++;
                continue;
            }

            try
            {
                await objectStore.DeleteObjectAsync(item.StorageKey, ct);
            }
            catch (StorageException ex) when (ex.Kind == StorageErrorKind.Retryable)
            {
                await MarkRetryableAsync(item, ex.Code, now, ct);
                retried++;
                metrics.RecordDeletionFailure(ex.Code);
                Log.Retryable(logger, item.FileId, ex.Code);
                continue;
            }
            catch (StorageException ex) when (ex.Kind == StorageErrorKind.Permanent)
            {
                await MarkPermanentAsync(item, ex.Code, now, ct);
                failedPermanent++;
                metrics.RecordDeletionFailure(ex.Code);
                Log.Permanent(logger, item.FileId, ex.Code);
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unknown failures are treated as retryable so the file is never stranded silently.
                await MarkRetryableAsync(item, "s3_error", now, ct);
                retried++;
                metrics.RecordDeletionFailure("s3_error");
                Log.Unexpected(logger, item.FileId, ex);
                continue;
            }

            await MarkDeletedAsync(item, now, ct);
            deleted++;
            metrics.RecordFileDeleted();
        }

        if (deleted + retried + failedPermanent + skippedLegalHold > 0)
        {
            Log.Summary(logger, deleted, retried, failedPermanent, skippedLegalHold);
        }

        return new DeletionSummary(deleted, retried, failedPermanent, skippedLegalHold);
    }

    private async Task<bool> IsHeldAsync(Guid fileId, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var held = await deletions.IsLegalHoldAsync(fileId, ct);
            await unitOfWork.CommitAsync(ct);
            return held;
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task MarkDeletedAsync(DeletionWorkItem item, DateTimeOffset now, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var affected = await deletions.MarkDeletedAsync(item.FileId, now, ct);
            if (affected == 0)
            {
                // A legal hold (or another state change) landed after the S3 delete; log and audit
                // defensively without flipping the file to deleted.
                await unitOfWork.RollbackAsync(ct);
                Log.HeldAfterDelete(logger, item.FileId);
                return;
            }

            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = item.FileId,
                ActorType = ActorType.System,
                Action = "deleted",
                Result = AuditResult.Success,
                Reason = "physical_deletion",
                Metadata = JsonSerializer.Serialize(new { priorStatus = "pending_deletion" }),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "file.deleted",
                AggregateType = FileAggregateType,
                AggregateId = item.FileId,
                Payload = JsonSerializer.Serialize(new { fileId = item.FileId }),
            }, ct);

            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task MarkRetryableAsync(DeletionWorkItem item, string error, DateTimeOffset now, CancellationToken ct)
    {
        var nextAttempt = now + DeletionBackoff.BackoffDelay(item.Attempts + 1, Random.Shared);

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await deletions.MarkRetryableFailureAsync(item.FileId, error, nextAttempt, ct);
            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = item.FileId,
                ActorType = ActorType.System,
                Action = "deletion_failed",
                Result = AuditResult.Failure,
                Reason = "retryable_storage_failure",
                Metadata = JsonSerializer.Serialize(new { error = SanitizeForAudit(error), attempts = item.Attempts + 1 }),
            }, ct);

            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task MarkPermanentAsync(DeletionWorkItem item, string error, DateTimeOffset now, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await deletions.MarkPermanentFailureAsync(item.FileId, error, ct);
            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = item.FileId,
                ActorType = ActorType.System,
                Action = "deletion_failed",
                Result = AuditResult.Failure,
                Reason = "permanent_storage_failure",
                Metadata = JsonSerializer.Serialize(new { error = SanitizeForAudit(error) }),
            }, ct);

            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static string SanitizeForAudit(string error) =>
        // last_deletion_error is already a stable, sanitized token; keep audit metadata minimal.
        error.Length > 80 ? error[..80] : error;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deletion pass: {Deleted} deleted, {Retried} retried, {FailedPermanent} permanent, {SkippedLegalHold} held")]
        public static partial void Summary(ILogger logger, int deleted, int retried, int failedPermanent, int skippedLegalHold);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Deletion of file {FileId} failed transiently ({Reason}); will retry")]
        public static partial void Retryable(ILogger logger, Guid fileId, string reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "Deletion of file {FileId} failed permanently ({Reason}); operator action required")]
        public static partial void Permanent(ILogger logger, Guid fileId, string reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected failure deleting file {FileId}")]
        public static partial void Unexpected(ILogger logger, Guid fileId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "File {FileId} gained a legal hold after its object was deleted; tombstone state left untouched")]
        public static partial void HeldAfterDelete(ILogger logger, Guid fileId);
    }
}
