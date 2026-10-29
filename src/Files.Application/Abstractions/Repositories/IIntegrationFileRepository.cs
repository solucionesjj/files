using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Keyset cursor for the integration file list (newest first on <c>created_at</c> + <c>id</c>).</summary>
public sealed record IntegrationFileCursor(DateTimeOffset CreatedAt, Guid Id);

/// <summary>
/// Persistence for integration file retrieval. Ownership/access lookups return <c>null</c> for
/// foreign files so callers surface a uniform 404 (no existence oracle, ADR-0012); the list is
/// owner-scoped and can never return portal/human files.
/// </summary>
public interface IIntegrationFileRepository
{
    /// <summary>Returns the file when owned by or actively granted to the integration, else <c>null</c>.</summary>
    Task<FileEntity?> GetAccessibleAsync(Guid fileId, Guid integrationId, CancellationToken ct = default);

    /// <summary>Owner-scoped page (newest first); integration-owned files only.</summary>
    Task<IReadOnlyList<FileEntity>> ListOwnedAsync(
        Guid integrationId,
        IReadOnlyList<FileStatus>? statuses,
        IntegrationFileCursor? cursor,
        int limit,
        CancellationToken ct = default);

    /// <summary>Whether an active (non-revoked, non-expired) grant exists for this file and integration.</summary>
    Task<bool> HasActiveGrantAsync(Guid fileId, Guid integrationId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Marks a completed integration download: increments <c>files.successful_downloads</c>/
    /// <c>last_download_at</c> and appends a <c>completed</c> download event (source/actor =
    /// integration) in one transaction.
    /// </summary>
    Task RecordCompletedDownloadAsync(Guid fileId, Guid integrationId, long bytesSent, CancellationToken ct = default);

    /// <summary>Appends a denied/failed integration download event (no counter increment).</summary>
    Task RecordDownloadEventAsync(Guid? fileId, Guid integrationId, string result, string reasonCode, CancellationToken ct = default);
}
