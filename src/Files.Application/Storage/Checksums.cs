using System.Security.Cryptography;

namespace Files.Application.Storage;

/// <summary>Pure SHA-256 helpers shared by upload verification (chunk and whole-file checksums).</summary>
public static class Checksums
{
    /// <summary>Whether <paramref name="value"/> is a 64-character lowercase/uppercase hex SHA-256 digest.</summary>
    public static bool IsHexSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    /// <summary>Normalizes a checksum for storage, or returns <c>null</c> for a blank value.</summary>
    public static string? Normalize(string? checksum) =>
        string.IsNullOrWhiteSpace(checksum) ? null : checksum.ToLowerInvariant();

    /// <summary>Lowercase hex SHA-256 of the given bytes.</summary>
    public static string ComputeHex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Lowercase hex SHA-256 of a stream's remaining content (async).</summary>
    public static async Task<string> ComputeHexAsync(Stream stream, CancellationToken ct = default) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
}
