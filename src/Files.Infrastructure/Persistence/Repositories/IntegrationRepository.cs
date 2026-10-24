using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class IntegrationRepository(FilesDbContext db) : IIntegrationRepository
{
    public void Add(Integration integration) => db.Integrations.Add(integration);

    public Task<Integration?> GetByEntraClientIdAsync(Guid clientId, CancellationToken ct = default) =>
        db.Integrations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.EntraClientId == clientId, ct);

    public Task<Integration?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Integrations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Slug == slug, ct);
}
