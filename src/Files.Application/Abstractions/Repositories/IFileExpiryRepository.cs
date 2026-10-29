namespace Files.Application.Abstractions.Repositories;

/// <summary>Expiration-job persistence: claims past-expiry files that are not on legal hold.</summary>
public interface IFileExpiryRepository
{
    /// <summary>
    /// Atomically claims up to <paramref name="batch"/> <c>available</c> files whose <c>expires_at</c>
    /// has passed and that are not on legal hold, ordered by expiry (<c>FOR UPDATE SKIP LOCKED</c>).
    /// </summary>
    Task<IReadOnlyList<Guid>> ClaimExpiredIdsAsync(int batch, DateTimeOffset now, CancellationToken ct = default);
}
