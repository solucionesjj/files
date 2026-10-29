using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class EmailTemplateRepository(FilesDbContext db) : IEmailTemplateRepository
{
    public async Task<IReadOnlyList<EmailTemplate>> ListActiveAsync(CancellationToken ct = default) =>
        await db.EmailTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Key)
            .ThenBy(t => t.Locale)
            .ToListAsync(ct);

    public Task<EmailTemplate?> GetActiveAsync(string key, string locale, CancellationToken ct = default) =>
        db.EmailTemplates.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Key == key && t.Locale == locale && t.IsActive, ct);

    public Task<EmailTemplate?> GetActiveTrackedAsync(string key, string locale, CancellationToken ct = default) =>
        db.EmailTemplates.SingleOrDefaultAsync(t => t.Key == key && t.Locale == locale && t.IsActive, ct);

    public Task<EmailTemplate?> GetNewestActiveAsync(string key, CancellationToken ct = default) =>
        db.EmailTemplates.AsNoTracking()
            .Where(t => t.Key == key && t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public void Add(EmailTemplate emailTemplate) => db.EmailTemplates.Add(emailTemplate);
}
