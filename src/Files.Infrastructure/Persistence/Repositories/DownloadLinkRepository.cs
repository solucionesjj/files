using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IDownloadLinkRepository"/> (S7 delete path only).</summary>
public sealed class DownloadLinkRepository(FilesDbContext db) : IDownloadLinkRepository
{
    public async Task<int> RevokeActiveForFileAsync(
        Guid fileId,
        Guid revokedBy,
        DateTimeOffset revokedAt,
        CancellationToken ct = default)
    {
        var active = await db.DownloadLinks
            .Where(l => l.FileId == fileId && l.Status == LinkStatus.Active)
            .ToListAsync(ct);

        foreach (var link in active)
        {
            link.Status = LinkStatus.Revoked;
            link.RevokedAt = revokedAt;
            link.RevokedBy = revokedBy;
        }

        return active.Count;
    }
}
