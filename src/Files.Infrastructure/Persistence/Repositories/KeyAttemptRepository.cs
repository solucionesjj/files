using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Dapper implementation of the abuse-counter persistence (operational tables, not EF-mapped).</summary>
public sealed class KeyAttemptRepository(FilesDbContext db) : IKeyAttemptRepository
{
    private const string AddSql =
        "INSERT INTO download_key_attempts (link_id, ip_hash, outcome) VALUES (@LinkId, @IpHash, @Outcome)";

    private const string CountLinkIpSql =
        "SELECT count(*) FROM download_key_attempts WHERE link_id = @LinkId AND ip_hash = @IpHash AND outcome = 'failed' AND attempted_at > @Since";

    private const string CountLinkSql =
        "SELECT count(*) FROM download_key_attempts WHERE link_id = @LinkId AND outcome = 'failed' AND attempted_at > @Since";

    private const string LockedUntilSql =
        "SELECT locked_until FROM abuse_lockouts WHERE scope_type = @ScopeType AND scope_key = @ScopeKey AND locked_until > now()";

    private const string UpsertLockoutSql =
        """
        INSERT INTO abuse_lockouts (scope_type, scope_key, locked_until, reason)
        VALUES (@ScopeType, @ScopeKey, @LockedUntil, @Reason)
        ON CONFLICT (scope_type, scope_key) DO UPDATE
           SET locked_until = GREATEST(abuse_lockouts.locked_until, EXCLUDED.locked_until),
               reason = EXCLUDED.reason,
               updated_at = now()
        """;

    public async Task AddAsync(Guid linkId, string ipHash, string outcome, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            AddSql, new { LinkId = linkId, IpHash = ipHash, Outcome = outcome }, cancellationToken: ct));
    }

    public async Task<int> CountLinkIpAsync(Guid linkId, string ipHash, DateTimeOffset since, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountLinkIpSql, new { LinkId = linkId, IpHash = ipHash, Since = since }, cancellationToken: ct));
    }

    public async Task<int> CountLinkAsync(Guid linkId, DateTimeOffset since, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountLinkSql, new { LinkId = linkId, Since = since }, cancellationToken: ct));
    }

    public async Task<DateTimeOffset?> GetLockedUntilAsync(string scopeType, string scopeKey, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var lockedUntil = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            LockedUntilSql, new { ScopeType = scopeType, ScopeKey = scopeKey }, cancellationToken: ct));
        return lockedUntil is null ? null : new DateTimeOffset(lockedUntil.Value, TimeSpan.Zero);
    }

    public async Task UpsertLockoutAsync(
        string scopeType,
        string scopeKey,
        DateTimeOffset lockedUntil,
        string reason,
        CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            UpsertLockoutSql,
            new { ScopeType = scopeType, ScopeKey = scopeKey, LockedUntil = lockedUntil, Reason = reason },
            cancellationToken: ct));
    }

    private async Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection;
    }
}
