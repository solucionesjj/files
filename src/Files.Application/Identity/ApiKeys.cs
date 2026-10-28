using System.Security.Cryptography;
using System.Text;

namespace Files.Application.Identity;

/// <summary>
/// Pure helpers for Files-issued API keys (ADR-0014). The key is
/// <c>fsk_&lt;12 base62&gt;.&lt;43 base64url&gt;</c>: the prefix enables an indexed lookup and the
/// secret provides 256 bits of entropy. Only <see cref="Hash"/> is persisted; verification is
/// constant-time.
/// </summary>
public static class ApiKeys
{
    public const string KeyPrefix = "fsk_";

    public const char Separator = '.';

    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    private const int PrefixRandomLength = 12;

    private const int HashLength = 32;

    public static string GeneratePrefix()
    {
        var bytes = RandomNumberGenerator.GetBytes(PrefixRandomLength);
        var chars = new char[PrefixRandomLength];
        for (var i = 0; i < PrefixRandomLength; i++)
        {
            chars[i] = PrefixAlphabet[bytes[i] % PrefixAlphabet.Length];
        }

        return KeyPrefix + new string(chars);
    }

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Format(string prefix, string secret) => prefix + Separator + secret;

    /// <summary>Returns the lookup prefix before the first separator, or <c>null</c> for a malformed key.</summary>
    public static string? TryGetPrefix(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var index = key.IndexOf(Separator);
        return index <= 0 ? null : key[..index];
    }

    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    public static bool Verify(string key, byte[] expectedHash) =>
        expectedHash.Length == HashLength
        && CryptographicOperations.FixedTimeEquals(Hash(key), expectedHash);
}
