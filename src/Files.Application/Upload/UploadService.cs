using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Integrations;
using Files.Application.Storage;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Files.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Files.Application.Upload;

/// <summary>
/// Default <see cref="IUploadService"/> implementation (ADR-0003). Session creation and chunk
/// writes enforce the staging reserve and the per-host concurrency limit; completion verifies the
/// assembled file (size + optional whole-file SHA-256) and then, when malware scanning is enabled,
/// publishes <c>processing</c>/<c>scan_status=pending</c> for the scan/commit worker; otherwise it
/// commits to S3 immediately (single <c>PutObject</c> or multipart) and verifies with
/// <c>HeadObject</c>. A failed commit retains the staging file so the client retries
/// <c>complete</c> without re-uploading.
/// </summary>
public sealed partial class UploadService(
    IUploadSessionRepository sessions,
    IUploadChunkRepository chunks,
    IUploadChunkWriter chunkWriter,
    IFileRepository files,
    IUnitOfWork unitOfWork,
    IChunkStore chunkStore,
    IObjectStore objectStore,
    IUploadCommitter committer,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider,
    StorageOptions storage,
    S3Options s3,
    IConfiguration configuration,
    IIntegrationQuotaService integrationQuota,
    ILogger<UploadService> logger) : IUploadService
{
    private const string FileAggregateType = "file";

    public async Task<ServiceResult<UploadSessionCreated>> CreatePortalAsync(
        CreatePortalUpload command,
        Guid ownerUserId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var validate = ValidateCreate(command.FileName, command.SizeBytes, command.ContentType, command.ChecksumSha256);
        if (validate is not null)
        {
            return validate;
        }

        if (!TryResolvePortalRetention(command.Retention, out var retention, out var retentionError))
        {
            return retentionError!;
        }

        return await CreateAsync(
            command.FileName, command.SizeBytes, command.ContentType, command.ChecksumSha256,
            retention, ownerUserId, ownerIntegrationId: null, traceId, ipHash, ct);
    }

    public async Task<ServiceResult<UploadSessionCreated>> CreateIntegrationAsync(
        CreateIntegrationUpload command,
        Guid ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var validate = ValidateCreate(command.FileName, command.SizeBytes, command.ContentType, command.ChecksumSha256);
        if (validate is not null)
        {
            return validate;
        }

        // Per-integration quotas (FR-INT-010): max file size, concurrent sessions, daily upload bytes.
        var quotaError = await integrationQuota.CheckCreateSessionAsync(ownerIntegrationId, command.SizeBytes, ct);
        if (quotaError is not null)
        {
            return ServiceResult<UploadSessionCreated>.Fail(quotaError);
        }

        return await CreateAsync(
            command.FileName, command.SizeBytes, command.ContentType, command.ChecksumSha256,
            Retention.Indefinite(), ownerUserId: null, ownerIntegrationId, traceId, ipHash, ct);
    }

    public async Task<ServiceResult<ChunkAccepted>> WriteChunkAsync(
        Guid sessionId,
        int chunkNumber,
        Stream body,
        long contentLength,
        string? checksumSha256,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (session is null)
        {
            return ServiceResult<ChunkAccepted>.Fail(404, "upload_session_not_found", "Upload session not found");
        }

        var stateError = ValidateWritableState(session);
        if (stateError is not null)
        {
            return stateError;
        }

        if (!UploadGeometry.IsChunkNumberInRange(chunkNumber, session.ChunkCount))
        {
            return ServiceResult<ChunkAccepted>.Fail(422, "chunk_out_of_range", "Chunk number is outside the session range");
        }

        if (contentLength > session.ChunkSizeBytes)
        {
            return ServiceResult<ChunkAccepted>.Fail(413, "chunk_too_large", "Chunk body exceeds the configured chunk size");
        }

        var offset = UploadGeometry.OffsetForChunk(chunkNumber, session.ChunkSizeBytes);
        var expectedSize = UploadGeometry.ExpectedChunkSize(chunkNumber, session.ChunkCount, session.ExpectedSizeBytes, session.ChunkSizeBytes);
        if (contentLength != expectedSize)
        {
            return ServiceResult<ChunkAccepted>.Fail(422, "chunk_out_of_range", "Chunk size does not match the expected chunk geometry");
        }

        if (checksumSha256 is not null && !Checksums.IsHexSha256(checksumSha256))
        {
            return ServiceResult<ChunkAccepted>.Fail(422, "chunk_checksum_mismatch", "Invalid chunk checksum format");
        }

        // Idempotency: a repeated chunk with the same size/hash succeeds without rewriting bytes.
        var existing = await chunks.GetAsync(sessionId, chunkNumber, ct);
        if (existing is not null)
        {
            if (existing.SizeBytes == contentLength
                && (existing.ChecksumSha256 is null || checksumSha256 is null
                    || string.Equals(existing.ChecksumSha256, checksumSha256, StringComparison.OrdinalIgnoreCase)))
            {
                return ServiceResult<ChunkAccepted>.Ok(await AcceptedAsync(sessionId, chunkNumber, ct));
            }

            return ServiceResult<ChunkAccepted>.Fail(409, "chunk_conflict", "Chunk number already exists with different content");
        }

        // Enforce the staging reserve before accepting any bytes (fail closed).
        if (!StagingCapacity.Allows(chunkStore.GetFreeBytes(storage.StagingPath), storage.StagingReserveBytes, contentLength))
        {
            return ServiceResult<ChunkAccepted>.Fail(507, "staging_capacity_exceeded", "Staging disk capacity exhausted");
        }

        string? actualChecksum;
        try
        {
            actualChecksum = await StageChunkAsync(session.StagingPath, offset, body, contentLength, checksumSha256, ct);
        }
        catch (ChunkContentException)
        {
            return ServiceResult<ChunkAccepted>.Fail(422, "chunk_checksum_mismatch", "Chunk checksum does not match");
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            var result = await chunkWriter.WriteChunkAsync(
                sessionId, chunkNumber, offset, contentLength, actualChecksum, ct);

            if (!result.Inserted)
            {
                // Lost a race to another in-flight write of the same chunk: treat as idempotent success.
                await unitOfWork.CommitAsync(ct);
                return ServiceResult<ChunkAccepted>.Ok(await AcceptedAsync(sessionId, chunkNumber, ct));
            }

            await unitOfWork.CommitAsync(ct);
            return ServiceResult<ChunkAccepted>.Ok(new ChunkAccepted(
                sessionId, chunkNumber, result.ReceivedBytes,
                await chunks.CountBySessionAsync(sessionId, ct), session.ChunkCount, result.Status));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ServiceResult<UploadStatusView>> GetStatusAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (session is null)
        {
            return ServiceResult<UploadStatusView>.Fail(404, "upload_session_not_found", "Upload session not found");
        }

        var received = await chunks.ListReceivedNumbersAsync(sessionId, ct);
        return ServiceResult<UploadStatusView>.Ok(new UploadStatusView(
            session.FileId, session.Id, session.Status, session.ChunkSizeBytes, session.ChunkCount,
            session.ReceivedBytes, received, session.ExpiresAt, session.LastError));
    }

    public async Task<ServiceResult<CompletedUpload>> CompleteAsync(
        Guid sessionId,
        string? checksumSha256,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (session is null)
        {
            return ServiceResult<CompletedUpload>.Fail(404, "upload_session_not_found", "Upload session not found");
        }

        // Idempotent: an already-completed session returns its file metadata.
        if (session.Status == UploadSessionStatus.Completed)
        {
            var file = await LoadOwnedFileAsync(session.FileId, ownerUserId, ownerIntegrationId, ct);
            return file is null
                ? ServiceResult<CompletedUpload>.Fail(404, "file_not_found", "File not found")
                : ServiceResult<CompletedUpload>.Ok(new CompletedUpload(file, session));
        }

        if (session.Status == UploadSessionStatus.Aborted)
        {
            return ServiceResult<CompletedUpload>.Fail(409, "upload_session_aborted", "Upload session was aborted");
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return ServiceResult<CompletedUpload>.Fail(410, "upload_session_expired", "Upload session has expired");
        }

        // Verify the assembled file before any S3 call (size + optional whole-file checksum).
        var verification = await VerifyAsync(session, checksumSha256, ct);
        if (verification is not null)
        {
            return verification;
        }

        // Publish intent: committing + processing. A crash here is retryable (staging retained).
        var trackedSession = await LoadOwnedSessionTrackedAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (trackedSession is null)
        {
            return ServiceResult<CompletedUpload>.Fail(404, "upload_session_not_found", "Upload session not found");
        }

        var fileEntity = await files.GetByIdTrackedAsync(trackedSession.FileId, ct);
        if (fileEntity is null)
        {
            return ServiceResult<CompletedUpload>.Fail(404, "file_not_found", "File not found");
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            trackedSession.Status = UploadSessionStatus.Committing;
            trackedSession.LastError = null;
            fileEntity.Status = FileStatus.Processing;

            if (storage.MalwareScanning)
            {
                // Worker model (ADR-0003): hand off to the scan/commit worker without scanning here.
                fileEntity.ScanStatus = ScanStatus.Pending;
                await WriteFileAuditAsync(fileEntity.Id, ownerUserId, ownerIntegrationId, "file.upload.scan_pending", traceId: null, ipHash: null, ct);
                await WriteOutboxAsync(fileEntity.Id, "file.upload.scan_pending", ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        if (storage.MalwareScanning)
        {
            var pendingFile = await LoadOwnedFileAsync(fileEntity.Id, ownerUserId, ownerIntegrationId, ct);
            return ServiceResult<CompletedUpload>.Ok(new CompletedUpload(pendingFile ?? fileEntity, trackedSession));
        }

        // Scanning disabled: commit to S3 (single PutObject or multipart), verify with HeadObject, then finalize.
        try
        {
            await committer.CommitAsync(fileEntity, trackedSession, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.S3CommitFailed(logger, sessionId, ex);
            await MarkCommitFailedAsync(trackedSession, ct);
            return ServiceResult<CompletedUpload>.Fail(502, "upload_commit_failed", "The upload could not be committed to storage; retry complete without re-uploading");
        }

        await committer.FinalizeAsync(fileEntity, trackedSession, ct);

        var completedFile = await LoadOwnedFileAsync(fileEntity.Id, ownerUserId, ownerIntegrationId, ct);
        return ServiceResult<CompletedUpload>.Ok(new CompletedUpload(completedFile ?? fileEntity, trackedSession));
    }

    public async Task<ServiceResult<bool>> AbortAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (session is null)
        {
            return ServiceResult<bool>.Fail(404, "upload_session_not_found", "Upload session not found");
        }

        if (session.Status == UploadSessionStatus.Aborted)
        {
            return ServiceResult<bool>.Ok(true);
        }

        if (session.Status == UploadSessionStatus.Completed)
        {
            return ServiceResult<bool>.Fail(409, "upload_session_completed", "Upload session is already completed");
        }

        if (session.S3UploadId is not null)
        {
            try
            {
                await objectStore.AbortMultipartAsync(ObjectKey(session.FileId), session.S3UploadId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.MultipartAbortFailed(logger, sessionId, ex);
            }
        }

        var tracked = await LoadOwnedSessionTrackedAsync(sessionId, ownerUserId, ownerIntegrationId, ct);
        if (tracked is not null)
        {
            try
            {
                await unitOfWork.BeginTransactionAsync(ct);
                tracked.Status = UploadSessionStatus.Aborted;
                tracked.S3UploadId = null;
                await chunks.DeleteBySessionAsync(sessionId, ct);
                await WriteFileAuditAsync(tracked.FileId, ownerUserId, ownerIntegrationId, "file.upload.aborted", traceId, ipHash, ct);
                await unitOfWork.SaveChangesAsync(ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        chunkStore.Delete(session.StagingPath);
        return ServiceResult<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ create

    private async Task<ServiceResult<UploadSessionCreated>> CreateAsync(
        string fileName,
        long sizeBytes,
        string contentType,
        string? checksumSha256,
        Retention retention,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var openSessions = await sessions.CountOpenAsync(now, ct);
        if (openSessions >= storage.MaxConcurrentSessions)
        {
            return ServiceResult<UploadSessionCreated>.Fail(507, "staging_capacity_exceeded", "Maximum concurrent upload sessions reached");
        }

        // Reserve the declared size plus one S3 part headroom up front (ADR-0003 §3.4).
        var headroom = sizeBytes + storage.S3PartSizeBytes;
        if (!StagingCapacity.Allows(chunkStore.GetFreeBytes(storage.StagingPath), storage.StagingReserveBytes, headroom))
        {
            return ServiceResult<UploadSessionCreated>.Fail(507, "staging_capacity_exceeded", "Staging disk capacity exhausted");
        }

        var chunkCount = UploadGeometry.ComputeChunkCount(sizeBytes, storage.ChunkSizeBytes);
        if (chunkCount > storage.MaxChunkCount)
        {
            return ServiceResult<UploadSessionCreated>.Fail(422, "file_too_large", "File size exceeds the maximum chunk count");
        }

        var normalizedChecksum = Checksums.Normalize(checksumSha256);

        var file = ownerUserId is not null
            ? FileEntity.CreateHuman(ownerUserId.Value, fileName, s3.Bucket, string.Empty, sizeBytes, contentType, retention)
            : FileEntity.CreateIntegration(ownerIntegrationId!.Value, fileName, s3.Bucket, string.Empty, sizeBytes, contentType);
        file.StorageKey = ObjectKey(file.Id);
        file.ChecksumSha256 = normalizedChecksum;

        var session = new UploadSession
        {
            FileId = file.Id,
            InitiatorType = ownerUserId is not null ? UploadInitiatorType.User : UploadInitiatorType.Integration,
            InitiatorId = ownerUserId ?? ownerIntegrationId!.Value,
            Status = UploadSessionStatus.Initiated,
            ExpectedSizeBytes = sizeBytes,
            ExpectedContentType = contentType,
            ExpectedChecksumSha256 = normalizedChecksum,
            ChunkSizeBytes = storage.ChunkSizeBytes,
            ChunkCount = chunkCount,
            ExpiresAt = now.AddMinutes(storage.UploadSessionTtlMinutes),
        };
        session.StagingPath = StagingPath(session.Id);

        try
        {
            await chunkStore.CreateAsync(session.StagingPath, ct);
        }
        catch (IOException ex)
        {
            Log.StagingCreateFailed(logger, ex);
            return ServiceResult<UploadSessionCreated>.Fail(503, "staging_write_failed", "Could not create the staging file");
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            files.Add(file);
            sessions.Add(session);
            // Flush the inserts first so the file_audit_events foreign key to files(id) is satisfied.
            await unitOfWork.SaveChangesAsync(ct);
            await WriteFileAuditAsync(file.Id, ownerUserId, ownerIntegrationId, "file.upload.session_created", traceId, ipHash, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            chunkStore.Delete(session.StagingPath);
            throw;
        }

        return ServiceResult<UploadSessionCreated>.Ok(new UploadSessionCreated(
            file.Id, session.Id, session.ChunkSizeBytes, session.ChunkCount, session.ExpiresAt, session.ReceivedBytes));
    }

    // ------------------------------------------------------------------ validation

    private static ServiceResult<UploadSessionCreated>? ValidateCreate(
        string fileName, long sizeBytes, string contentType, string? checksumSha256)
    {
        if (!FileNamePolicy.IsValid(fileName))
        {
            return ServiceResult<UploadSessionCreated>.Fail(422, "invalid_filename", "Invalid file name");
        }

        if (sizeBytes < 0)
        {
            return ServiceResult<UploadSessionCreated>.Fail(422, "validation_error", "sizeBytes must be zero or positive");
        }

        if (!ContentTypePolicy.IsValid(contentType))
        {
            return ServiceResult<UploadSessionCreated>.Fail(422, "unsupported_content_type", "Invalid content type");
        }

        if (checksumSha256 is not null && !Checksums.IsHexSha256(checksumSha256))
        {
            return ServiceResult<UploadSessionCreated>.Fail(422, "validation_error", "checksumSha256 must be 64 hex characters");
        }

        return null;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<UploadSession?> LoadOwnedSessionAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct) =>
        ownerUserId is not null
            ? await sessions.GetOwnedByUserAsync(sessionId, ownerUserId.Value, ct)
            : await sessions.GetOwnedByIntegrationAsync(sessionId, ownerIntegrationId!.Value, ct);

    private async Task<UploadSession?> LoadOwnedSessionTrackedAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct)
    {
        var session = await sessions.GetByIdTrackedAsync(sessionId, ct);
        if (session is null)
        {
            return null;
        }

        var owned = ownerUserId is not null
            ? session.InitiatorType == UploadInitiatorType.User && session.InitiatorId == ownerUserId.Value
            : session.InitiatorType == UploadInitiatorType.Integration && session.InitiatorId == ownerIntegrationId!.Value;
        return owned ? session : null;
    }

    private async Task<FileEntity?> LoadOwnedFileAsync(
        Guid fileId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct) =>
        ownerUserId is not null
            ? await files.GetOwnedFileAsync(fileId, ownerUserId.Value, ct)
            : await files.GetOwnedIntegrationFileAsync(fileId, ownerIntegrationId!.Value, ct);

    private ServiceResult<ChunkAccepted>? ValidateWritableState(UploadSession session)
    {
        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return ServiceResult<ChunkAccepted>.Fail(410, "upload_session_expired", "Upload session has expired");
        }

        return session.Status switch
        {
            UploadSessionStatus.Initiated or UploadSessionStatus.Uploading or UploadSessionStatus.Received => null,
            UploadSessionStatus.Completed => ServiceResult<ChunkAccepted>.Fail(409, "upload_session_completed", "Upload session is already completed"),
            UploadSessionStatus.Aborted => ServiceResult<ChunkAccepted>.Fail(409, "upload_session_aborted", "Upload session was aborted"),
            _ => ServiceResult<ChunkAccepted>.Fail(409, "conflict", "Upload session is not accepting chunks"),
        };
    }

    private async Task<string?> StageChunkAsync(
        string stagingPath,
        long offset,
        Stream body,
        long contentLength,
        string? checksumSha256,
        CancellationToken ct)
    {
        // When a checksum is supplied, buffer the (bounded) chunk to verify it before any byte is
        // committed to disk; otherwise stream straight through and count the written bytes.
        if (checksumSha256 is not null)
        {
            using var buffer = new MemoryStream((int)contentLength);
            await body.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            var actualHash = Checksums.ComputeHex(bytes);
            if (!string.Equals(actualHash, checksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChunkContentException();
            }

            using var reader = new MemoryStream(bytes, writable: false);
            await chunkStore.WriteAsync(stagingPath, offset, reader, contentLength, ct);
            return actualHash;
        }

        var written = await chunkStore.WriteAsync(stagingPath, offset, body, contentLength, ct);
        if (written != contentLength)
        {
            throw new ChunkContentException();
        }

        return null;
    }

    private async Task<ChunkAccepted> AcceptedAsync(Guid sessionId, int chunkNumber, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct);
        var count = await chunks.CountBySessionAsync(sessionId, ct);
        return new ChunkAccepted(sessionId, chunkNumber, session?.ReceivedBytes ?? 0, count, session?.ChunkCount ?? 0, session?.Status ?? UploadSessionStatus.Initiated);
    }

    private async Task<ServiceResult<CompletedUpload>?> VerifyAsync(
        UploadSession session,
        string? suppliedChecksum,
        CancellationToken ct)
    {
        var received = await chunks.ListBySessionAsync(session.Id, ct);
        if (received.Count != session.ChunkCount)
        {
            return ServiceResult<CompletedUpload>.Fail(422, "upload_incomplete", "Not all chunks have been received");
        }

        long total = 0;
        foreach (var chunk in received.OrderBy(c => c.ChunkNumber))
        {
            var expectedOffset = UploadGeometry.OffsetForChunk(chunk.ChunkNumber, session.ChunkSizeBytes);
            var expectedSize = UploadGeometry.ExpectedChunkSize(chunk.ChunkNumber, session.ChunkCount, session.ExpectedSizeBytes, session.ChunkSizeBytes);
            if (chunk.OffsetBytes != expectedOffset || chunk.SizeBytes != expectedSize)
            {
                return ServiceResult<CompletedUpload>.Fail(422, "upload_incomplete", "Chunk offsets or sizes are inconsistent");
            }

            total += chunk.SizeBytes;
        }

        if (total != session.ExpectedSizeBytes)
        {
            return ServiceResult<CompletedUpload>.Fail(422, "upload_size_mismatch", "Staged size does not match the declared size");
        }

        var expectedChecksum = session.ExpectedChecksumSha256 ?? Checksums.Normalize(suppliedChecksum);
        if (expectedChecksum is not null)
        {
            string actual;
            try
            {
                using var stream = chunkStore.OpenRead(session.StagingPath);
                actual = await Checksums.ComputeHexAsync(stream, ct);
            }
            catch (IOException)
            {
                return ServiceResult<CompletedUpload>.Fail(503, "staging_write_failed", "Could not read the staged file");
            }

            if (!string.Equals(actual, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<CompletedUpload>.Fail(422, "upload_checksum_mismatch", "Whole-file checksum does not match");
            }
        }

        return null;
    }

    private async Task MarkCommitFailedAsync(UploadSession session, CancellationToken ct)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            session.Status = UploadSessionStatus.Committing;
            session.LastError = "S3 commit failed";
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private bool TryResolvePortalRetention(RetentionInput? input, out Retention retention, out ServiceResult<UploadSessionCreated>? error)
    {
        var defaultDays = configuration.GetValue("Retention:DefaultDays", 30);
        var maxDays = configuration.GetValue("Retention:MaxDays", 365);

        if (input is not null && string.Equals(input.Mode, "indefinite", StringComparison.Ordinal))
        {
            retention = Retention.Indefinite();
            error = ServiceResult<UploadSessionCreated>.Fail(422, "retention_not_allowed", "Indefinite retention is not allowed for portal files");
            return false;
        }

        var days = input?.Days ?? defaultDays;
        if (days is < 1 || days > maxDays)
        {
            retention = Retention.Indefinite();
            error = ServiceResult<UploadSessionCreated>.Fail(422, "retention_exceeds_maximum", $"Retention must be between 1 and {maxDays} days");
            return false;
        }

        if (days > defaultDays && string.IsNullOrWhiteSpace(input?.Reason))
        {
            retention = Retention.Indefinite();
            error = ServiceResult<UploadSessionCreated>.Fail(422, "retention_reason_required", "A reason is required when extending retention beyond the default");
            return false;
        }

        retention = Retention.Finite(timeProvider.GetUtcNow().AddDays(days), days);
        error = null;
        return true;
    }

    private async Task WriteFileAuditAsync(
        Guid fileId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string action,
        string? traceId,
        string? ipHash,
        CancellationToken ct)
    {
        var actorType = ownerUserId is not null ? ActorType.User : ActorType.Integration;
        var actorId = ownerUserId ?? ownerIntegrationId;
        await auditWriter.WriteFileAuditAsync(new AuditEvent
        {
            FileId = fileId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            Result = AuditResult.Success,
            Metadata = "{}",
            TraceId = traceId,
            IpHash = ipHash,
        }, ct);
    }

    private async Task WriteOutboxAsync(Guid fileId, string eventType, CancellationToken ct)
    {
        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = eventType,
            AggregateType = FileAggregateType,
            AggregateId = fileId,
            Payload = JsonSerializer.Serialize(new { fileId }),
        }, ct);
    }

    private static string ObjectKey(Guid fileId) => $"f/{fileId:D}";

    private string StagingPath(Guid sessionId) =>
        Path.Combine(storage.StagingPath, $"{sessionId:D}.part");

    /// <summary>Thrown internally when a chunk's checksum or byte length is wrong.</summary>
    private sealed class ChunkContentException : Exception;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "S3 commit failed for upload session {SessionId}")]
        public static partial void S3CommitFailed(ILogger logger, Guid sessionId, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to abort multipart upload for session {SessionId}")]
        public static partial void MultipartAbortFailed(ILogger logger, Guid sessionId, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Staging file creation failed")]
        public static partial void StagingCreateFailed(ILogger logger, Exception exception);
    }
}
