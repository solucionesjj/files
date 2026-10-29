using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;

namespace Files.Application.Notifications;

/// <summary>Computes and persists due expiration-notification events (ADR-0006/0007).</summary>
public interface INotificationScheduler
{
    /// <summary>One scheduling pass; returns the number of events scheduled/revived.</summary>
    Task<int> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Schedules future due events from the active notification policy. Idempotent (unique on
/// file/interval/expiry); only <c>superseded</c> rows are revived. Retroactive intervals are skipped.
/// </summary>
public sealed class NotificationScheduler(
    INotificationPolicyRepository policies,
    INotificationEventRepository events,
    IPlatformSettingsRepository settings,
    IUnitOfWork unitOfWork) : INotificationScheduler
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (await settings.GetBoolAsync("notifications_enabled", ct) == false)
        {
            return 0;
        }

        var policy = await policies.GetActiveAsync(ct);
        if (policy is null || policy.IntervalsDays.Length == 0)
        {
            return 0;
        }

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var scheduled = await events.ScheduleAllAsync(policy.Id, policy.IntervalsDays, ct);
            await unitOfWork.CommitAsync(ct);
            return scheduled;
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }
}
