using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Storage;
using Files.Application.Upload;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Files.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Files.Application.Files;

/// <summary>Input for <c>updateMyFile</c> (rename / retag / retention; at least one field set).</summary>
public sealed record UpdateFileCommand(string? FileName, RetentionInput? Retention, IReadOnlyList<string>? Tags);

/// <summary>Portal file management: rename/tag/retention and logical deletion (S7 / ADR-0006/0008).</summary>
public interface IFileManagementService
{
    Task<ServiceResult<FileEntity>> UpdateAsync(
        Guid fileId,
        Guid ownerUserId,
        UpdateFileCommand command,
        int? ifMatch,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<bool>> DeleteAsync(
        Guid fileId,
        Guid ownerUserId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);
}

public sealed partial class FileManagementService(
    IFileRepository files,
    ITagRepository tags,
    IDownloadLinkRepository links,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<FileManagementService> logger) : IFileManagementService
{
    private const string FileAggregateType = "file";
    private const int MaxTagsPerFile = 20;

    public async Task<ServiceResult<FileEntity>> UpdateAsync(
        Guid fileId,
        Guid ownerUserId,
        UpdateFileCommand command,
        int? ifMatch,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        if (command.FileName is null && command.Retention is null && command.Tags is null)
        {
            return ServiceResult<FileEntity>.Fail(400, "validation_error", "At least one of fileName, retention, or tags is required");
        }

        var file = await files.GetByIdTrackedAsync(fileId, ct);
        if (file is null || file.OwnerUserId != ownerUserId)
        {
            return ServiceResult<FileEntity>.Fail(404, "file_not_found", "File not found");
        }

        if (ifMatch is { } expected && file.Version != expected)
        {
            return ServiceResult<FileEntity>.Fail(412, "stale_version", "The file was modified; reload and retry");
        }

        // Validate everything before opening the transaction (fail fast, no partial writes).
        if (command.FileName is { } fileName && !FileNamePolicy.IsValid(fileName))
        {
            return ServiceResult<FileEntity>.Fail(422, "invalid_filename", "Invalid file name");
        }

        var normalizedTags = command.Tags is { } tagNames ? NormalizeTags(tagNames) : null;
        if (command.Tags is not null && normalizedTags is null)
        {
            return ServiceResult<FileEntity>.Fail(400, "validation_error", $"At most {MaxTagsPerFile} non-empty tags of 64 characters or fewer are allowed");
        }

        string? retentionReason = null;
        Retention? retention = null;
        if (command.Retention is { } retentionInput)
        {
            if (string.Equals(retentionInput.Mode, "indefinite", StringComparison.Ordinal))
            {
                return ServiceResult<FileEntity>.Fail(422, "retention_not_allowed", "Indefinite retention is not allowed for portal files");
            }

            var defaultDays = configuration.GetValue("Retention:DefaultDays", RetentionRules.DefaultDays);
            var maxDays = configuration.GetValue("Retention:MaxDays", RetentionRules.MaxDays);
            var error = RetentionRules.ResolveFinite(
                retentionInput.Days, retentionInput.Reason, file.LegalHold, timeProvider.GetUtcNow(),
                out retention, defaultDays, maxDays);

            if (error is not null)
            {
                return ServiceResult<FileEntity>.Fail(error);
            }

            retentionReason = retentionInput.Reason?.Trim() is { Length: > 0 } reason ? reason : null;
        }

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var changed = new List<string>(3);
            var fileMutated = false;

            if (command.FileName is { } newName && !string.Equals(file.OriginalFilename, newName, StringComparison.Ordinal))
            {
                file.OriginalFilename = newName;
                changed.Add("fileName");
                fileMutated = true;
            }

            if (retention is not null)
            {
                file.ApplyRetention(retention);
                file.RetentionReason = retentionReason;
                changed.Add("retention");
                fileMutated = true;
            }

            if (normalizedTags is not null)
            {
                await tags.ReplaceFileTagsAsync(fileId, ownerUserId, normalizedTags, ct);
                changed.Add("tags");
            }

            if (!fileMutated)
            {
                // Only tags changed (file_tags has no version token): bump the file version so the
                // returned ETag / If-Match still reflect the retag.
                files.MarkUpdated(file);
            }

            await WriteFileAuditAsync(
                file.Id, ActorType.User, ownerUserId, "file.updated",
                JsonSerializer.Serialize(new { changed }),
                retentionReason, traceId, ipHash, ct);
            await WriteOutboxAsync(file.Id, "file.updated", ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        return ServiceResult<FileEntity>.Ok(file);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid fileId,
        Guid ownerUserId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default)
    {
        var file = await files.GetByIdTrackedAsync(fileId, ct);
        if (file is null || file.OwnerUserId != ownerUserId)
        {
            return ServiceResult<bool>.Fail(404, "file_not_found", "File not found");
        }

        if (file.LegalHold)
        {
            return ServiceResult<bool>.Fail(409, "legal_hold_active", "Legal hold blocks deletion");
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Logical deletion first (ADR-0008): tombstone + revoke links + recompute access_mode.
            // Physical deletion (S3 DeleteObject) is an M6 worker concern and is not done here.
            file.Status = FileStatus.PendingDeletion;
            file.DeletionRequestedByType = DeletionRequesterType.User;
            file.DeletionRequestedBy = ownerUserId;
            file.NextDeletionAt = now;
            file.AccessMode = AccessMode.OwnerOnly;

            await links.RevokeActiveForFileAsync(fileId, ownerUserId, now, ct);

            await WriteFileAuditAsync(
                file.Id, ActorType.User, ownerUserId, "file.deletion_requested",
                JsonSerializer.Serialize(new { priorStatus = "available" }),
                reason: null, traceId, ipHash, ct);
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

    private static List<string>? NormalizeTags(IReadOnlyList<string> tagNames)
    {
        if (tagNames.Count > MaxTagsPerFile)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(tagNames.Count);
        foreach (var raw in tagNames)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 64 || !seen.Add(name))
            {
                continue;
            }

            normalized.Add(name);
        }

        return normalized;
    }

    private async Task WriteFileAuditAsync(
        Guid fileId,
        ActorType actorType,
        Guid? actorId,
        string action,
        string metadata,
        string? reason,
        string? traceId,
        string? ipHash,
        CancellationToken ct)
    {
        await auditWriter.WriteFileAuditAsync(new AuditEvent
        {
            FileId = fileId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            Result = AuditResult.Success,
            Reason = reason,
            Metadata = metadata,
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
        [LoggerMessage(Level = LogLevel.Information, Message = "File {FileId} logically deleted; physical deletion queued")]
        public static partial void FileDeleted(ILogger logger, Guid fileId);
    }
}
