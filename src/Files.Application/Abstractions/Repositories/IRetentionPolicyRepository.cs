using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Versioned global retention configuration (ADR-0006).</summary>
public interface IRetentionPolicyRepository
{
    /// <summary>The single active policy, or <c>null</c> when none is active.</summary>
    Task<RetentionPolicy?> GetActiveAsync(CancellationToken ct = default);

    /// <summary>All versions, newest first (for the history view).</summary>
    Task<IReadOnlyList<RetentionPolicy>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Any policy by id (used to validate a migration target).</summary>
    Task<RetentionPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant used to deactivate the current version when publishing a new one.</summary>
    Task<RetentionPolicy?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    void Add(RetentionPolicy policy);
}
