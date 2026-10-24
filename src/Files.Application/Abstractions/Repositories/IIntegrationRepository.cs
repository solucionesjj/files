using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for machine consumers. Lookups are keyed on machine-identity claims.</summary>
public interface IIntegrationRepository
{
    void Add(Integration integration);

    /// <summary>Resolves an integration by its Entra client-id claim (machine auth).</summary>
    Task<Integration?> GetByEntraClientIdAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Resolves an integration by its admin-facing slug.</summary>
    Task<Integration?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
