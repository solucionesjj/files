using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Abstractions.Repositories;

/// <summary>An atomically claimed (issued → redeemed) grant.</summary>
public sealed record GrantClaim(Guid GrantId, Guid FileId, GrantSource Source, Guid? IssuedToUserId);

/// <summary>
/// Download-grant persistence. Reads/claims are raw SQL so the single-use transition
/// <c>issued → redeemed</c> is atomic (ADR-0005); completion increments the file counter and writes
/// a <c>download_events</c> row in the same transaction, so a grant is counted exactly once.
/// </summary>
public interface IDownloadGrantRepository
{
    /// <summary>Registers a new (issued) grant in the ambient unit-of-work transaction.</summary>
    void Add(DownloadGrant grant);

    /// <summary>
    /// Atomically claims an unexpired <c>issued</c> grant (<c>issued → redeemed</c>), or returns
    /// <c>null</c> when the grant is unknown, expired, or already consumed. Runs in the ambient
    /// transaction and compares expiry against the server clock (<paramref name="now"/>).
    /// </summary>
    Task<GrantClaim?> ClaimAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Reads the grant state for classifying a non-claim, or <c>null</c> if unknown.</summary>
    Task<GrantState?> GetStateAsync(byte[] tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Marks a redeemed grant <c>completed</c>, increments <c>files.successful_downloads</c>, and
    /// inserts a <c>completed</c> download event — all in one transaction. Idempotent: a non-redeemed
    /// grant updates nothing (no double count).
    /// </summary>
    Task CompleteAsync(Guid grantId, Guid fileId, long bytesSent, CancellationToken ct = default);

    /// <summary>
    /// Marks an issued/redeemed grant <c>failed</c> and records a <c>failed</c> download event
    /// (used for unavailable files, missing objects, and aborted streams).
    /// </summary>
    Task FailAsync(Guid grantId, Guid fileId, string reasonCode, CancellationToken ct = default);
}
