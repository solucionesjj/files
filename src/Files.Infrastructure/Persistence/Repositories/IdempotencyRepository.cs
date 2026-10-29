using System.Data;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Idempotency-key persistence (FR-INT-007). Runs outside the unit-of-work transaction (it wraps
/// the endpoint), so it manages its own connection. The unique (scope, key) index makes the
/// insert-or-lose slot acquisition atomic.
/// </summary>
public sealed class IdempotencyRepository(FilesDbContext db) : IIdempotencyRepository
{
    private static readonly string BeginSql = SqlResources.Load("Sql.Idempotency.Begin.sql");
    private static readonly string LookupSql = SqlResources.Load("Sql.Idempotency.Lookup.sql");
    private static readonly string CompleteSql = SqlResources.Load("Sql.Idempotency.Complete.sql");
    private static readonly string AbandonSql = SqlResources.Load("Sql.Idempotency.Abandon.sql");

    public async Task<IdempotencyOutcome> BeginAsync(
        string scopeType,
        Guid scopeId,
        string key,
        string requestHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var recordId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(BeginSql, new { ScopeType = scopeType, ScopeId = scopeId, Key = key, RequestHash = requestHash, ExpiresAt = expiresAt }, cancellationToken: ct));

            if (recordId is { } id)
            {
                return new IdempotencyOutcome(IdempotencyOutcomeKind.Acquired, RecordId: id);
            }

            var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(
                new CommandDefinition(LookupSql, new { ScopeType = scopeType, ScopeId = scopeId, Key = key }, cancellationToken: ct));

            if (row is null)
            {
                continue;
            }

            if (row.State == "completed")
            {
                return row.RequestHash == requestHash
                    ? new IdempotencyOutcome(IdempotencyOutcomeKind.Replay, Status: row.ResponseStatus, Body: DecodeBody(row.ResponseBody), Headers: row.ResponseHeaders)
                    : new IdempotencyOutcome(IdempotencyOutcomeKind.Reuse);
            }

            if (new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc)) > now)
            {
                return new IdempotencyOutcome(IdempotencyOutcomeKind.InProgress);
            }

            // Stale in-progress record: reclaim it and retry the insert.
            await connection.ExecuteAsync(
                new CommandDefinition(AbandonSql, new { ScopeType = scopeType, ScopeId = scopeId, Key = key }, cancellationToken: ct));
        }

        return new IdempotencyOutcome(IdempotencyOutcomeKind.InProgress);
    }

    public async Task CompleteAsync(Guid recordId, int status, byte[]? body, string? headers, DateTimeOffset now, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            new CommandDefinition(CompleteSql, new { RecordId = recordId, Status = status, Body = EncodeBody(body), Headers = headers, Now = now }, cancellationToken: ct));
    }

    public async Task AbandonAsync(string scopeType, Guid scopeId, string key, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            new CommandDefinition(AbandonSql, new { ScopeType = scopeType, ScopeId = scopeId, Key = key }, cancellationToken: ct));
    }

    private async Task<IDbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection;
    }

    private static string? EncodeBody(byte[]? body) =>
        body is null or { Length: 0 } ? null : System.Text.Encoding.UTF8.GetString(body);

    private static byte[]? DecodeBody(string? body) =>
        string.IsNullOrEmpty(body) ? null : System.Text.Encoding.UTF8.GetBytes(body);

    private sealed record IdempotencyRow(
        Guid Id,
        string RequestHash,
        string State,
        int? ResponseStatus,
        string? ResponseBody,
        string? ResponseHeaders,
        DateTime ExpiresAt);
}
