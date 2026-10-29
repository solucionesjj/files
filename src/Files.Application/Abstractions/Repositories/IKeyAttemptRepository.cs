namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Persistence for the abuse counters (<c>download_key_attempts</c>, <c>abuse_lockouts</c>). Writes
/// autocommit (outside the grant transaction) so a failed key attempt is still recorded even when
/// the redeem operation answers 404.
/// </summary>
public interface IKeyAttemptRepository
{
    /// <summary>Records a key attempt (<c>failed</c> or <c>locked_out</c>).</summary>
    Task AddAsync(Guid linkId, string ipHash, string outcome, CancellationToken ct = default);

    /// <summary>Counts failed attempts for a link+IP since <paramref name="since"/>.</summary>
    Task<int> CountLinkIpAsync(Guid linkId, string ipHash, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Counts failed attempts for a link across all IPs since <paramref name="since"/>.</summary>
    Task<int> CountLinkAsync(Guid linkId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Returns the active lockout deadline for a scope, or <c>null</c> when none.</summary>
    Task<DateTimeOffset?> GetLockedUntilAsync(string scopeType, string scopeKey, CancellationToken ct = default);

    /// <summary>Creates or extends a lockout to at least <paramref name="lockedUntil"/>.</summary>
    Task UpsertLockoutAsync(
        string scopeType,
        string scopeKey,
        DateTimeOffset lockedUntil,
        string reason,
        CancellationToken ct = default);
}
