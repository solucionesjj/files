namespace Files.Application.Abstractions;

/// <summary>A file+session pair due for the scan/commit worker.</summary>
public sealed record ScanWorkItem(Guid FileId, Guid SessionId);

/// <summary>
/// Atomically claims the next scan/commit work items (D-35). Uses <c>FOR UPDATE SKIP LOCKED</c> so
/// concurrent workers never double-process a file, and stamps a short lease while the scan runs.
/// </summary>
public interface IScanWorkClaimer
{
    /// <summary>
    /// Claims up to <paramref name="limit"/> files that are <c>processing</c> with
    /// <c>scan_status IN ('pending', 'failed')</c> and due for a retry, marking each with a
    /// <paramref name="lease"/> so it is not reclaimed while being scanned.
    /// </summary>
    Task<IReadOnlyList<ScanWorkItem>> ClaimAsync(int limit, TimeSpan lease, CancellationToken ct = default);
}
