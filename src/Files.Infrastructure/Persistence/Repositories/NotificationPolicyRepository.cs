using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class NotificationPolicyRepository(FilesDbContext db) : INotificationPolicyRepository
{
    public Task<NotificationPolicy?> GetActiveAsync(CancellationToken ct = default) =>
        db.NotificationPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.IsActive, ct);

    public Task<NotificationPolicy?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.NotificationPolicies.SingleOrDefaultAsync(p => p.Id == id, ct);

    public void Add(NotificationPolicy policy) => db.NotificationPolicies.Add(policy);
}
