using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Versioned expiration-reminder schedule (ADR-0006/0007).</summary>
public interface INotificationPolicyRepository
{
    /// <summary>The single active policy, or <c>null</c> when none is active.</summary>
    Task<NotificationPolicy?> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Tracked variant used to deactivate the current version when publishing a new one.</summary>
    Task<NotificationPolicy?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    void Add(NotificationPolicy policy);
}
