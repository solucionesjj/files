using System.Security.Cryptography;
using System.Text;

namespace Files.Application.Deletion;

/// <summary>
/// Pure helpers for the reconciliation job's S3 key scope (FR-DEL-006). Files are stored under
/// <c>f/&lt;id&gt;</c>, orphaned objects move to <c>orphan/</c>, and any <c>staging/</c> object in
/// the current design (staging is local disk, ADR-0003) is orphaned by construction. Object keys
/// are never logged in full — only a fingerprint.
/// </summary>
public static class ReconciliationScope
{
    public const string FilePrefix = "f/";
    public const string StagingPrefix = "staging/";
    public const string OrphanPrefix = "orphan/";

    /// <summary>True for a committed file object under <c>f/</c>.</summary>
    public static bool IsFileObject(string key) => key.StartsWith(FilePrefix, StringComparison.Ordinal);

    /// <summary>True for a staging object under <c>staging/</c>.</summary>
    public static bool IsStagingObject(string key) => key.StartsWith(StagingPrefix, StringComparison.Ordinal);

    /// <summary>True for an already-moved orphan (never re-processed).</summary>
    public static bool IsOrphanObject(string key) => key.StartsWith(OrphanPrefix, StringComparison.Ordinal);

    /// <summary>The destination key when moving an orphan under <c>orphan/</c> (keeps the relative path).</summary>
    public static string OrphanDestinationKey(string key) => OrphanPrefix + key;

    /// <summary>
    /// A truncated SHA-256 fingerprint of a key for logs/metrics. Never returns the full key.
    /// </summary>
    public static string Fingerprint(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
