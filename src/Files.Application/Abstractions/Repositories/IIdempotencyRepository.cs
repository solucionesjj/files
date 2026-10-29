namespace Files.Application.Abstractions.Repositories;

/// <summary>How an idempotency <c>BeginAsync</c> resolved for a (scope, key) pair.</summary>
public enum IdempotencyOutcomeKind
{
    /// <summary>The caller now owns an in-progress record and should execute the request.</summary>
    Acquired,

    /// <summary>A completed record with a matching request hash exists; replay the stored response.</summary>
    Replay,

    /// <summary>A completed record with a different request hash exists → 422 idempotency_key_reuse.</summary>
    Reuse,

    /// <summary>The original request is still running → 409 idempotency_in_progress.</summary>
    InProgress,
}

public sealed record IdempotencyOutcome(
    IdempotencyOutcomeKind Kind,
    Guid? RecordId = null,
    int? Status = null,
    byte[]? Body = null,
    string? Headers = null);

/// <summary>
/// Idempotency-key persistence (<c>idempotency_records</c>, FR-INT-007). Scoped per
/// (scope_type, scope_id, key); the unique index guarantees at most one in-progress or completed
/// record per key, and the request hash distinguishes a safe retry from a key reuse.
/// </summary>
public interface IIdempotencyRepository
{
    Task<IdempotencyOutcome> BeginAsync(
        string scopeType,
        Guid scopeId,
        string key,
        string requestHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken ct = default);

    Task CompleteAsync(
        Guid recordId,
        int status,
        byte[]? body,
        string? headers,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Removes an in-progress record (used when the request fails with a 5xx so a retry re-runs).</summary>
    Task AbandonAsync(string scopeType, Guid scopeId, string key, CancellationToken ct = default);
}
