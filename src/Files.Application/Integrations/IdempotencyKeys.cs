using System.Security.Cryptography;
using System.Text;

namespace Files.Application.Integrations;

/// <summary>
/// Pure helpers for the <c>Idempotency-Key</c> header (FR-INT-007). The key is scoped to the
/// calling integration and retained 24 h; the request hash binds a key to the exact request it
/// first authorized, so a reused key with a different body is rejected with
/// <c>idempotency_key_reuse</c>.
/// </summary>
public static class IdempotencyKeys
{
    public const int MinLength = 8;

    public const int MaxLength = 128;

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static bool IsValid(string? key) =>
        key is not null && key.Length is >= MinLength and <= MaxLength;

    /// <summary>SHA-256 (hex) of <c>method</c> + <c>path</c> + <c>body</c>, the idempotency discriminator.</summary>
    public static string Hash(string method, string path, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{method}\n{path}\n");
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
