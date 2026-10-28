using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ITagRepository"/> (user-scoped, citext tags).</summary>
public sealed class TagRepository(FilesDbContext db) : ITagRepository
{
    public async Task<IReadOnlyList<string>> ReplaceFileTagsAsync(
        Guid fileId,
        Guid ownerUserId,
        IReadOnlyList<string> names,
        CancellationToken ct = default)
    {
        // Get-or-create user-scoped tags. The `citext` column + the (scope, owner_user_id, name)
        // unique index make matching case-insensitive and collisions impossible within one request.
        var tagIds = new List<Guid>(names.Count);
        foreach (var name in names)
        {
            var tag = await db.Tags.SingleOrDefaultAsync(
                t => t.Scope == TagScope.User && t.OwnerUserId == ownerUserId && t.Name == name, ct);

            if (tag is null)
            {
                tag = new Tag { Scope = TagScope.User, OwnerUserId = ownerUserId, Name = name, CreatedBy = ownerUserId };
                db.Tags.Add(tag);
            }

            tagIds.Add(tag.Id);
        }

        // Replace the whole set (PATCH semantics): drop existing joins, insert the new ones.
        var existing = await db.FileTags.Where(ft => ft.FileId == fileId).ToListAsync(ct);
        db.FileTags.RemoveRange(existing);
        foreach (var tagId in tagIds)
        {
            db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId, CreatedBy = ownerUserId });
        }

        return names;
    }

    public async Task<IReadOnlyList<string>> ListFileTagsAsync(Guid fileId, CancellationToken ct = default) =>
        await db.FileTags
            .AsNoTracking()
            .Where(ft => ft.FileId == fileId)
            .OrderBy(ft => ft.Tag.Name)
            .Select(ft => ft.Tag.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ListTagsByFileIdsAsync(
        IReadOnlyList<Guid> fileIds,
        CancellationToken ct = default)
    {
        if (fileIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var rows = await db.FileTags
            .AsNoTracking()
            .Where(ft => fileIds.Contains(ft.FileId))
            .OrderBy(ft => ft.Tag.Name)
            .Select(ft => new { ft.FileId, ft.Tag.Name })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.FileId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).ToList());
    }
}
