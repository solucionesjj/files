using System.Security.Cryptography;

namespace Files.Application.Files;

/// <summary>
/// Download-grant token codec (ADR-0005): 32 random bytes from a CSPRNG, base64url-encoded (43
/// chars, no padding) as the capability URL token, with only <c>SHA-256(token bytes)</c> stored at
/// rest in <c>download_grants.grant_token_hash</c>. Tokens are never logged.
/// </summary>
public static class GrantTokens
{
    public const int ByteLength = 32;
    public const int TokenLength = 43; // base64url of 32 bytes, unpadded.

    public static string Generate() => Base64UrlEncode(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>Hashes the token for storage/lookup (SHA-256 of the decoded 32 bytes).</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Base64UrlDecode(token));

    /// <summary>
    /// Validates the token shape and returns its SHA-256 hash for lookup. Returns <c>false</c> for
    /// anything that cannot be a grant token (so the API answers a uniform 404).
    /// </summary>
    public static bool TryDecode(string? token, out byte[] tokenHash)
    {
        tokenHash = [];
        if (string.IsNullOrWhiteSpace(token) || token.Length != TokenLength)
        {
            return false;
        }

        try
        {
            var bytes = Base64UrlDecode(token);
            if (bytes.Length != ByteLength)
            {
                return false;
            }

            tokenHash = SHA256.HashData(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
