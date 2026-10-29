using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Files.Application.Upload;

/// <summary>
/// Default <see cref="IUploadCommitter"/>. Performs the server-side S3 commit (single
/// <c>PutObject</c> or multipart, then <c>HeadObject</c> verification) and the terminal file/session
/// transitions. S3 failures throw so the caller can record <c>upload_commit_failed</c> and retry
/// without re-uploading chunks; staging is retained until the object is verified.
/// </summary>
public sealed partial class UploadCommitter(
    IObjectStore objectStore,
    IChunkStore chunkStore,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    IDailyUsageRepository dailyUsage,
    TimeProvider timeProvider,
    StorageOptions storage,
    ILogger<UploadCommitter> logger) : IUploadCommitter
{
    private const string FileAggregateType = "file";

    public async Task CommitAsync(FileEntity file, UploadSession session, CancellationToken ct = default)
    {
        var key = file.StorageKey
            ?? throw new InvalidOperationException("A file being committed must have a storage key.");

        if (file.SizeBytes <= storage.S3SinglePutThresholdBytes)
        {
            using var stream = chunkStore.OpenRead(session.StagingPath);
            await objectStore.PutObjectAsync(key, stream, file.SizeBytes, file.ContentType, ct).ConfigureAwait(false);
        }
        else
        {
            // Retry path: abandon any previous partial multipart upload and start fresh.
            if (session.S3UploadId is not null)
            {
                try
                {
                    await objectStore.AbortMultipartAsync(key, session.S3UploadId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.StaleMultipartAbortFailed(logger, session.Id, ex);
                }

                session.S3UploadId = null;
            }

            var uploadId = await objectStore.InitiateMultipartAsync(key, file.ContentType, ct).ConfigureAwait(false);
            session.S3UploadId = uploadId;
            session.S3PartSizeBytes = storage.S3PartSizeBytes;

            var parts = new List<UploadedPart>();
            var partNumber = 1;
            long remaining = file.SizeBytes;

            using var stream = chunkStore.OpenRead(session.StagingPath);
            var buffer = new byte[storage.S3PartSizeBytes];
            while (remaining > 0)
            {
                var partSize = (int)Math.Min(storage.S3PartSizeBytes, remaining);
                var read = 0;
                while (read < partSize)
                {
                    var count = stream.Read(buffer, read, partSize - read);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                if (read != partSize)
                {
                    throw new InvalidOperationException("Staging file ended prematurely during multipart upload");
                }

                using var part = new MemoryStream(buffer, 0, partSize, writable: false);
                parts.Add(await objectStore.UploadPartAsync(key, uploadId, partNumber, part, partSize, ct).ConfigureAwait(false));
                remaining -= partSize;
                partNumber++;
            }

            await objectStore.CompleteMultipartAsync(key, uploadId, parts, ct).ConfigureAwait(false);
            session.S3UploadId = null;
        }

        var head = await objectStore.HeadObjectAsync(key, ct).ConfigureAwait(false);
        if (!head.Exists || head.SizeBytes != file.SizeBytes)
        {
            throw new InvalidOperationException("S3 object verification failed after commit");
        }
    }

    public async Task FinalizeAsync(FileEntity file, UploadSession session, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
            file.Status = FileStatus.Available;
            file.AvailableAt = now;
            file.ContentTypeSource = ContentTypeSource.Declared;
            file.ScanStatus = storage.MalwareScanning ? ScanStatus.Clean : ScanStatus.NotScanned;
            file.ScanCompletedAt = storage.MalwareScanning ? now : null;
            file.ScanNextAttemptAt = null;
            file.ChecksumSha256 ??= session.ExpectedChecksumSha256;
            session.Status = UploadSessionStatus.Completed;
            session.CompletedAt = now;
            session.LastError = null;
            session.S3UploadId = null;
            await WriteFileAuditAsync(file.Id, session, "file.upload.completed", ct).ConfigureAwait(false);
            await WriteOutboxAsync(file.Id, "file.upload.completed", ct).ConfigureAwait(false);

            // Count integration upload bytes only once the object is verified and the file is
            // available (FR-INT-010); human uploads are not part of the integration quota scope.
            if (session.InitiatorType == UploadInitiatorType.Integration)
            {
                await dailyUsage.AddUploadedAsync(session.InitiatorId, file.SizeBytes, DateOnly.FromDateTime(now.Date), ct).ConfigureAwait(false);
            }

            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await unitOfWork.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        chunkStore.Delete(session.StagingPath);
    }

    public async Task QuarantineAsync(FileEntity file, UploadSession session, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
            file.Status = FileStatus.Quarantined;
            file.ScanStatus = ScanStatus.Infected;
            file.ScanCompletedAt = now;
            file.ScanNextAttemptAt = null;
            file.QuarantineReason = "Malware detected during upload scan";
            session.Status = UploadSessionStatus.Completed;
            session.CompletedAt = now;
            session.S3UploadId = null;
            await WriteFileAuditAsync(file.Id, session, "file.upload.quarantined", ct).ConfigureAwait(false);
            await WriteOutboxAsync(file.Id, "file.upload.quarantined", ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await unitOfWork.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        chunkStore.Delete(session.StagingPath);
    }

    public async Task MarkScanFailedAsync(FileEntity file, UploadSession session, CancellationToken ct = default)
    {
        // The session is deliberately left untouched: its `updated_at` must keep pointing at the
        // completion time so the 24 h cleanup window still closes (ADR-0003 §6).
        var attempt = file.ScanAttempts + 1;
        try
        {
            await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
            file.ScanStatus = ScanStatus.Failed;
            file.Status = FileStatus.Processing;
            file.ScanAttempts = attempt;
            file.ScanNextAttemptAt = timeProvider.GetUtcNow() + ScanBackoff.DelayFor(attempt);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await unitOfWork.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task WriteFileAuditAsync(Guid fileId, UploadSession session, string action, CancellationToken ct)
    {
        var actorType = session.InitiatorType == UploadInitiatorType.User ? ActorType.User : ActorType.Integration;
        await auditWriter.WriteFileAuditAsync(new AuditEvent
        {
            FileId = fileId,
            ActorType = actorType,
            ActorId = session.InitiatorId,
            Action = action,
            Result = AuditResult.Success,
            Metadata = "{}",
        }, ct).ConfigureAwait(false);
    }

    private async Task WriteOutboxAsync(Guid fileId, string eventType, CancellationToken ct)
    {
        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = eventType,
            AggregateType = FileAggregateType,
            AggregateId = fileId,
            Payload = JsonSerializer.Serialize(new { fileId }),
        }, ct).ConfigureAwait(false);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to abort stale multipart upload for session {SessionId}")]
        public static partial void StaleMultipartAbortFailed(ILogger logger, Guid sessionId, Exception exception);
    }
}
