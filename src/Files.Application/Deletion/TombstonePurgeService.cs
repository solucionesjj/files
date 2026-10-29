using System.Text.Json;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Application.Deletion;

/// <summary>One tombstone-purge pass summary (ADR-0008).</summary>
public sealed record TombstonePurgeSummary(int Purged);

/// <summary>Nulls the storage key on tombstones past the audit retention window (ADR-0008).</summary>
public interface ITombstonePurgeService
{
    Task<TombstonePurgeSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// <c>deleted</c> metadata is retained 60 months for audit; at purge the <c>storage_key</c> is
/// nulled only (the tombstone row and audit trail remain). This does NOT claim immediate complete
/// erasure — logical backups may retain copies for up to 35 days (FR-DEL-008).
/// </summary>
public sealed partial class TombstonePurgeService(
    IFileDeletionRepository deletions,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    ILogger<TombstonePurgeService> logger) : ITombstonePurgeService
{
    private const int TombstoneRetentionMonths = 60;

    public async Task<TombstonePurgeSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow().AddMonths(-TombstoneRetentionMonths);
        var purged = 0;

        while (true)
        {
            IReadOnlyList<Guid> ids;
            await unitOfWork.BeginTransactionAsync(ct);
            try
            {
                ids = await deletions.ClaimPurgeableAsync(scheduler.DeletionBatchSize, cutoff, ct);

                foreach (var fileId in ids)
                {
                    var affected = await deletions.NullStorageKeyAsync(fileId, ct);
                    if (affected == 0)
                    {
                        continue;
                    }

                    await auditWriter.WriteFileAuditAsync(new AuditEvent
                    {
                        FileId = fileId,
                        ActorType = ActorType.System,
                        Action = "tombstone_purged",
                        Result = AuditResult.Success,
                        Reason = "retention_purge",
                        Metadata = JsonSerializer.Serialize(new { retentionMonths = TombstoneRetentionMonths }),
                    }, ct);

                    purged++;
                }

                await unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await unitOfWork.RollbackAsync(ct);
                throw;
            }

            if (ids.Count < scheduler.DeletionBatchSize)
            {
                break;
            }
        }

        if (purged > 0)
        {
            Log.Purged(logger, purged);
        }

        return new TombstonePurgeSummary(purged);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Tombstone purge nulled storage keys on {Count} deleted files")]
        public static partial void Purged(ILogger logger, int count);
    }
}
