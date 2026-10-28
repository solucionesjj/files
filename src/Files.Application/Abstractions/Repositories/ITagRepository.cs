using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Tag persistence for the portal. Tags are case-insensitive (<c>citext</c>) and unique per
/// <c>(scope, owner_user_id, name)</c>; portal tags are user-scoped (US-007 "in my scope").
/// Retagging replaces the file's whole tag set.
/// </summary>
public interface ITagRepository
{
    /// <summary>
    /// Replaces the file's tags with <paramref name="names"/> (get-or-create user tags, deduplicated
    /// case-insensitively) and returns the normalized names in input order.
    /// </summary>
    Task<IReadOnlyList<string>> ReplaceFileTagsAsync(
        Guid fileId,
        Guid ownerUserId,
        IReadOnlyList<string> names,
        CancellationToken ct = default);

    /// <summary>Returns the tag names attached to a single file.</summary>
    Task<IReadOnlyList<string>> ListFileTagsAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Returns a fileId → tag-names map for the given files (one query for a list page).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ListTagsByFileIdsAsync(
        IReadOnlyList<Guid> fileIds,
        CancellationToken ct = default);
}
