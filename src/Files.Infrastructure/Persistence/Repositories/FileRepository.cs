using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class FileRepository(FilesDbContext db, TimeProvider timeProvider) : IFileRepository
{
    public void Add(FileEntity file) => db.Files.Add(file);

    public Task<FileEntity?> GetByIdAsync(Guid fileId, CancellationToken ct = default) =>
        db.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, ct);

    public Task<FileEntity?> GetByIdTrackedAsync(Guid fileId, CancellationToken ct = default) =>
        db.Files.SingleOrDefaultAsync(f => f.Id == fileId, ct);

    public void MarkUpdated(FileEntity file) => db.Entry(file).State = EntityState.Modified;

    public Task<FileEntity?> GetOwnedFileAsync(Guid fileId, Guid ownerUserId, CancellationToken ct = default) =>
        db.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == ownerUserId, ct);

    public Task<FileEntity?> GetOwnedIntegrationFileAsync(
        Guid fileId,
        Guid ownerIntegrationId,
        CancellationToken ct = default) =>
        db.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.OwnerIntegrationId == ownerIntegrationId, ct);

    public async Task<IReadOnlyList<FileEntity>> ListOwnedAsync(
        Guid ownerUserId,
        FileListQuery query,
        FileListCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        // Owner-scoped: only human-owned files. Integration files (owner_user_id IS NULL) and other
        // users' files are excluded by the predicate, so the portal can never see them (ADR-0014/0012).
        IQueryable<FileEntity> files = db.Files
            .AsNoTracking()
            .Where(f => f.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            files = files.Where(f => EF.Functions.ILike(f.OriginalFilename, $"%{query.Search}%"));
        }

        if (query.Tags.Count > 0)
        {
            files = files.Where(f => query.Tags.All(tag =>
                db.FileTags.Any(ft => ft.FileId == f.Id && ft.Tag.Name == tag)));
        }

        if (query.Statuses is { Count: > 0 })
        {
            files = files.Where(f => query.Statuses.Contains(f.Status));
        }

        if (query.ExpiresBefore is not null)
        {
            files = files.Where(f => f.ExpiresAt != null && f.ExpiresAt < query.ExpiresBefore);
        }

        if (query.ExpiresAfter is not null)
        {
            files = files.Where(f => f.ExpiresAt != null && f.ExpiresAt > query.ExpiresAfter);
        }

        if (query.ExpiringWithinDays is not null)
        {
            var cutoff = timeProvider.GetUtcNow().AddDays(query.ExpiringWithinDays.Value);
            files = files.Where(f => f.ExpiresAt != null && f.ExpiresAt <= cutoff);
        }

        files = query.Sort switch
        {
            FileSort.CreatedAtDesc => OrderByCreatedAt(files, descending: true, cursor),
            FileSort.Name => OrderByName(files, descending: false, cursor),
            FileSort.NameDesc => OrderByName(files, descending: true, cursor),
            FileSort.Downloads => OrderByDownloads(files, descending: false, cursor),
            FileSort.DownloadsDesc => OrderByDownloads(files, descending: true, cursor),
            _ => OrderByCreatedAt(files, descending: false, cursor),
        };

        return await files.Take(limit).ToListAsync(ct);
    }

    private static IQueryable<FileEntity> OrderByCreatedAt(
        IQueryable<FileEntity> files,
        bool descending,
        FileListCursor? cursor)
    {
        if (cursor?.CreatedAt is { } createdAt)
        {
            files = descending
                ? files.Where(f => f.CreatedAt < createdAt || (f.CreatedAt == createdAt && f.Id < cursor.Id))
                : files.Where(f => f.CreatedAt > createdAt || (f.CreatedAt == createdAt && f.Id > cursor.Id));
        }

        return descending
            ? files.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            : files.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id);
    }

    private static IQueryable<FileEntity> OrderByName(
        IQueryable<FileEntity> files,
        bool descending,
        FileListCursor? cursor)
    {
        if (cursor?.Name is { } name)
        {
            files = descending
                ? files.Where(f => f.OriginalFilename.CompareTo(name) < 0
                    || (f.OriginalFilename == name && f.Id < cursor.Id))
                : files.Where(f => f.OriginalFilename.CompareTo(name) > 0
                    || (f.OriginalFilename == name && f.Id > cursor.Id));
        }

        return descending
            ? files.OrderByDescending(f => f.OriginalFilename).ThenByDescending(f => f.Id)
            : files.OrderBy(f => f.OriginalFilename).ThenBy(f => f.Id);
    }

    private static IQueryable<FileEntity> OrderByDownloads(
        IQueryable<FileEntity> files,
        bool descending,
        FileListCursor? cursor)
    {
        if (cursor?.Downloads is { } downloads)
        {
            files = descending
                ? files.Where(f => f.SuccessfulDownloads < downloads
                    || (f.SuccessfulDownloads == downloads && f.Id < cursor.Id))
                : files.Where(f => f.SuccessfulDownloads > downloads
                    || (f.SuccessfulDownloads == downloads && f.Id > cursor.Id));
        }

        return descending
            ? files.OrderByDescending(f => f.SuccessfulDownloads).ThenByDescending(f => f.Id)
            : files.OrderBy(f => f.SuccessfulDownloads).ThenBy(f => f.Id);
    }
}
