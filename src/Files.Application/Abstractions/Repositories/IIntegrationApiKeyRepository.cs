using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for Files-issued integration API keys (ADR-0014).</summary>
public interface IIntegrationApiKeyRepository
{
    void Add(IntegrationApiKey key);

    /// <summary>Resolves a key by its public prefix (the secret is never queried).</summary>
    Task<IntegrationApiKey?> GetByPrefixAsync(string prefix, CancellationToken ct = default);

    Task<IntegrationApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant for revocation in the ambient transaction.</summary>
    Task<IntegrationApiKey?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationApiKey>> ListByIntegrationAsync(Guid integrationId, CancellationToken ct = default);

    /// <summary>Conditionally bumps <c>last_used_at</c> at most once per throttle window.</summary>
    Task UpdateLastUsedAsync(Guid id, DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default);
}
