namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Minimal download-link persistence for S7 (link creation/listing arrives with S8). Only the
/// logical-deletion path needs it today: revoking a file's active links (ADR-0008).
/// </summary>
public interface IDownloadLinkRepository
{
    /// <summary>Marks all active links for a file as revoked and returns the count.</summary>
    Task<int> RevokeActiveForFileAsync(
        Guid fileId,
        Guid revokedBy,
        DateTimeOffset revokedAt,
        CancellationToken ct = default);
}
