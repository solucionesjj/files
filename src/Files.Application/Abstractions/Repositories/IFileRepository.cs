using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Persistence for file metadata. Lookups are owner-scoped: a foreign file is returned as
/// <c>null</c> so callers can surface a uniform 404 (no existence oracle).
/// </summary>
public interface IFileRepository
{
    void Add(FileEntity file);

    /// <summary>Returns the file only when owned by <paramref name="ownerUserId"/>, else <c>null</c>.</summary>
    Task<FileEntity?> GetOwnedFileAsync(Guid fileId, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Returns the file only when owned by <paramref name="ownerIntegrationId"/>, else <c>null</c>.</summary>
    Task<FileEntity?> GetOwnedIntegrationFileAsync(
        Guid fileId,
        Guid ownerIntegrationId,
        CancellationToken ct = default);
}
