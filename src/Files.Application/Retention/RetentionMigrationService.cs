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

/// <summary>One migration worker pass summary (ADR-0006).</summary>
public sealed record RetentionMigrationRunSummary(Guid? MigrationId, long Processed, bool Completed);

/// <summary>Processes pending bulk retention migrations in bounded, resumable batches.</summary>
public interface IRetentionMigrationService
{
    Task<RetentionMigrationRunSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Claims the oldest pending migration and applies its target policy default to human-owned,
/// available, non-held files in pages; each file is audited and its notifications recalculated.
/// Integration files are always skipped (v1, ADR-0006).
/// </summary>
public sealed partial class RetentionMigrationService(
    IRetentionMigrationRepository migrations,
    IRetentionPolicyRepository retentionPolicies,
    INotificationPolicyRepository notificationPolicies,
    INotificationEventRepository notifications,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    ILogger<RetentionMigrationService> logger) : IRetentionMigrationService
{
    public async Task<RetentionMigrationRunSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        RetentionMigration? migration;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            migration = await migrations.ClaimNextAsync(now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        if (migration is null)
        {
            return new RetentionMigrationRunSummary(null, 0, false);
        }

        try
        {
            var target = await retentionPolicies.GetByIdAsync(migration.TargetPolicyId, ct);
            if (target is null)
            {
                await FailAsync(migration.Id, "target_policy_not_found", now, ct);
                return new RetentionMigrationRunSummary(migration.Id, 0, false);
            }

            var notificationPolicy = await notificationPolicies.GetActiveAsync(ct);
            var total = await migrations.CountInScopeAsync(ct);

            long processed = 0;
            var afterId = Guid.Empty;
            while (true)
            {
                IReadOnlyList<Guid> ids;
                await unitOfWork.BeginTransactionAsync(ct);
                try
                {
                    ids = await migrations.ApplyToNextPageAsync(
                        target.Id, target.DefaultDays, migration.Reason,
                        scheduler.MigrationBatchSize, afterId, ct);

                    foreach (var fileId in ids)
                    {
                        await notifications.SupersedeForFileAsync(fileId, ct);
                        if (notificationPolicy is not null)
                        {
                            await notifications.ScheduleForFileAsync(
                                fileId, notificationPolicy.Id, notificationPolicy.IntervalsDays, ct);
                        }

                        await auditWriter.WriteFileAuditAsync(new AuditEvent
                        {
                            FileId = fileId,
                            ActorType = ActorType.System,
                            Action = "retention_changed",
                            Result = AuditResult.Success,
                            Reason = "admin_retention_migration",
                            Metadata = JsonSerializer.Serialize(new { migrationId = migration.Id, reason = migration.Reason }),
                        }, ct);
                    }

                    await unitOfWork.CommitAsync(ct);
                }
                catch
                {
                    await unitOfWork.RollbackAsync(ct);
                    throw;
                }

                processed += ids.Count;
                if (ids.Count < scheduler.MigrationBatchSize)
                {
                    break;
                }

                afterId = ids[^1];
            }

            await unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await migrations.MarkCompletedAsync(migration.Id, total, processed, failed: 0, now, ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await unitOfWork.RollbackAsync(ct);
                throw;
            }

            Log.Completed(logger, migration.Id, processed);
            return new RetentionMigrationRunSummary(migration.Id, processed, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Failed(logger, migration.Id, ex);
            await FailAsync(migration.Id, ex.GetType().Name, now, ct);
            return new RetentionMigrationRunSummary(migration.Id, 0, false);
        }
    }

    private async Task FailAsync(Guid migrationId, string error, DateTimeOffset now, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await migrations.MarkFailedAsync(migrationId, error, now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Retention migration {MigrationId} completed ({Processed} files)")]
        public static partial void Completed(ILogger logger, Guid migrationId, long processed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Retention migration {MigrationId} failed")]
        public static partial void Failed(ILogger logger, Guid migrationId, Exception exception);
    }
}
