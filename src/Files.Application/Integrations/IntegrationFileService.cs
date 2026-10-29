using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Files.Application.Integrations;

/// <summary>Availability view for <c>getIntegrationFileAvailability</c> (no content transferred).</summary>
public sealed record FileAvailabilityView(
    Guid FileId,
    bool Available,
    long SizeBytes,
    DateTimeOffset? ExpiresAt,
    ScanStatus ScanStatus,
    DateTimeOffset CheckedAt);

/// <summary>
/// Integration file metadata and lifecycle (FR-INT-002/005, FR-M2M-007/011): owner-scoped list,
/// owned-or-granted read, owner-only logical delete, and a lightweight availability check.
/// </summary>
public interface IIntegrationFileService
{
    Task<ServiceResult<IReadOnlyList<FileEntity>>> ListAsync(
        Guid integrationId,
        IReadOnlyList<FileStatus>? statuses,
        IntegrationFileCursor? cursor,
        int limit,
        CancellationToken ct = default);

    Task<ServiceResult<FileEntity>> GetAsync(Guid fileId, Guid integrationId, CancellationToken ct = default);

    Task<ServiceResult<FileAvailabilityView>> GetAvailabilityAsync(Guid fileId, Guid integrationId, CancellationToken ct = default);

    Task<ServiceResult<bool>> DeleteAsync(
        Guid fileId,
        Guid integrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);
}

public sealed partial class IntegrationFileService(
    IIntegrationFileRepository integrationFiles,
    IFileRepository files,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider,
    ILogger<IntegrationFileService> logger) : IIntegrationFileService
{
    private const string FileAggregateType = "file";

    public async Task<ServiceResult<IReadOnlyList<FileEntity>>> ListAsync(
        Guid integrationId,
        IReadOnlyList<FileStatus>? statuses,
        IntegrationFileCursor? cursor,
        int limit,
        CancellationToken ct = default) =>
        ServiceResult<IReadOnlyList<FileEntity>>.Ok(
            await integrationFiles.ListOwnedAsync(integrationId, statuses, cursor, limit, ct));

    public async Task<ServiceResult<FileEntity>> GetAsync(Guid fileId, Guid integrationId, CancellationToken ct = default)
    {
        var file = await integrationFiles.GetAccessibleAsync(fileId, integrationId, ct);
        return file is null
            ? ServiceResult<FileEntity>.Fail(404, "file_not_found", "File not found")
            : ServiceResult<FileEntity>.Ok(file);
    }

    public async Task<ServiceResult<FileAvailabilityView>> GetAvailabilityAsync(Guid fileId, Guid integrationId, CancellationToken ct = default)
    {
        var file = await integrationFiles.GetAccessibleAsync(fileId, integrationId, ct);
        if (file is null)
        {
            return ServiceResult<FileAvailabilityView>.Fail(404, "file_not_found", "File not found");
        }

        var now = timeProvider.GetUtcNow();
        return ServiceResult<FileAvailabilityView>.Ok(new FileAvailabilityView(
            file.Id, FileDownloadability.IsDownloadable(file, now), file.SizeBytes, file.ExpiresAt, file.ScanStatus, now));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid fileId,
        Guid integrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var file = await files.GetByIdTrackedAsync(fileId, ct);
        if (file is null || file.OwnerIntegrationId != integrationId)
        {
            return ServiceResult<bool>.Fail(404, "file_not_found", "File not found");
        }

        // Idempotent (FR-DEL-003): a repeated delete of an already-deleted file succeeds.
        if (file.Status is FileStatus.PendingDeletion or FileStatus.Deleted)
        {
            return ServiceResult<bool>.Ok(true);
        }

        if (file.LegalHold)
        {
            return ServiceResult<bool>.Fail(409, "legal_hold_active", "Legal hold blocks deletion");
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            file.Status = FileStatus.PendingDeletion;
            file.DeletionRequestedByType = DeletionRequesterType.Integration;
            file.DeletionRequestedBy = integrationId;
            file.NextDeletionAt = now;

            await WriteFileAuditAsync(file.Id, integrationId, "file.deletion_requested", traceId, ipHash, ct);
            await WriteOutboxAsync(file.Id, "file.deletion_requested", ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        Log.FileDeleted(logger, fileId);
        return ServiceResult<bool>.Ok(true);
    }

    private async Task WriteFileAuditAsync(
        Guid fileId,
        Guid integrationId,
        string action,
        string? traceId,
        string? ipHash,
        CancellationToken ct)
    {
        await auditWriter.WriteFileAuditAsync(new AuditEvent
        {
            FileId = fileId,
            ActorType = ActorType.Integration,
            ActorId = integrationId,
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Integration file {FileId} logically deleted; physical deletion queued")]
        public static partial void FileDeleted(ILogger logger, Guid fileId);
    }
}
