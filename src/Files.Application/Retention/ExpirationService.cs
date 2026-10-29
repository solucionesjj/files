using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Notifications;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Files.Application.Lifecycle;

/// <summary>One expiration pass summary (FR-DEL-001/002/008, FR-RET-008).</summary>
public sealed record ExpirationSummary(int Expired);

/// <summary>Expires past-due files: blocks downloads and queues physical deletion (S11).</summary>
public interface IExpirationService
{
    Task<ExpirationSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Transitions <c>available</c> files past <c>expires_at</c> to <c>pending_deletion</c>, skipping
/// legal-hold files. Downloads are already blocked by the eligibility predicate; this makes the
/// state explicit, revokes links, cancels pending notifications, and audits (ADR-0008/0006).
/// </summary>
public sealed partial class ExpirationService(
    IFileExpiryRepository expiry,
    IFileRepository files,
    IDownloadLinkRepository links,
    INotificationEventRepository notifications,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    ILogger<ExpirationService> logger) : IExpirationService
{
    private const string FileAggregateType = "file";

    public async Task<ExpirationSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var expired = 0;

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var ids = await expiry.ClaimExpiredIdsAsync(scheduler.MigrationBatchSize, now, ct);
            foreach (var id in ids)
            {
                var file = await files.GetByIdTrackedAsync(id, ct);
                if (file is null || file.LegalHold)
                {
                    continue;
                }

                file.Status = FileStatus.PendingDeletion;
                file.DeletionRequestedByType = DeletionRequesterType.System;
                file.DeletionReason = "retention_expired";
                file.NextDeletionAt = now;
                file.AccessMode = AccessMode.OwnerOnly;

                await links.RevokeActiveForFileBySystemAsync(id, now, "file_expired", ct);
                await notifications.SupersedeForFileAsync(id, ct);

                await auditWriter.WriteFileAuditAsync(new AuditEvent
                {
                    FileId = id,
                    ActorType = ActorType.System,
                    Action = "expired",
                    Result = AuditResult.Success,
                    Reason = "retention_expired",
                    Metadata = JsonSerializer.Serialize(new { priorStatus = "available" }),
                }, ct);

                await outboxWriter.WriteAsync(new OutboxMessage
                {
                    EventType = "file.expired",
                    AggregateType = FileAggregateType,
                    AggregateId = id,
                    Payload = JsonSerializer.Serialize(new { fileId = id }),
                }, ct);

                expired++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        if (expired > 0)
        {
            Log.Expired(logger, expired);
        }

        return new ExpirationSummary(expired);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Expiration pass transitioned {Count} files to pending_deletion")]
        public static partial void Expired(ILogger logger, int count);
    }
}
