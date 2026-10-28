using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Persistence for machine consumers. Lookups are keyed on the admin-facing slug and id.</summary>
public interface IIntegrationRepository
{
    void Add(Integration integration);

    /// <summary>Resolves an integration by its admin-facing slug.</summary>
    Task<Integration?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Resolves an integration (with its DB permissions) by id, for admin operations.</summary>
    Task<Integration?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant used when an admin mutates the integration in the current transaction.</summary>
    Task<Integration?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Keyset-paginated listing ordered by <c>(created_at DESC, id DESC)</c>. Pass the previous
    /// page's last <c>created_at</c> as the cursor, or <c>null</c> for the first page.
    /// </summary>
    Task<IReadOnlyList<Integration>> ListAsync(
        IntegrationStatus? status,
        int limit,
        DateTimeOffset? cursorCreatedAt,
        CancellationToken ct = default);
}
