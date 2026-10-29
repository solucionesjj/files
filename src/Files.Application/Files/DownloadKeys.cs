using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Isopoh.Cryptography.Argon2;

namespace Files.Application.Files;

/// <summary>
/// Download-key generation and verification (ADR-0005). The key is exactly 8 characters from
/// <c>[A-Za-z0-9]</c>, generated with a CSPRNG, case-sensitive, and stored only as an
/// <c>Argon2id(pepper || key)</c> verifier with a per-key random salt. Parameters are the OWASP
/// baseline: m=19 MiB, t=2, p=1. The pepper is versioned (<c>download_key_version</c>); this class
/// verifies against the single configured pepper and its callers record the version.
/// </summary>
public static partial class DownloadKeys
{
    public const int KeyLength = 8;
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // Argon2id parameters (OWASP baseline, ADR-0005): memory in KiB, iterations, lanes, hash length.
    private const int MemoryCostKiB = 19 * 1024; // 19 MiB
    private const int TimeCost = 2;
    private const int Lanes = 1;
    private const int SaltLength = 16;
    private const int HashLengthBytes = 32;

    private static readonly Lazy<string> DummyHash = new(
        () => HashInternal("", "00000000", new byte[SaltLength]));

    [GeneratedRegex("^[A-Za-z0-9]{8}$")]
    private static partial Regex KeyShape();

    /// <summary>Generates an 8-character case-sensitive key from <c>[A-Za-z0-9]</c> via a CSPRNG.</summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, KeyLength);

    /// <summary>True when <paramref name="key"/> is exactly 8 characters of <c>[A-Za-z0-9]</c>.</summary>
    public static bool IsValidShape(string? key) => key is not null && KeyShape().IsMatch(key);

    /// <summary>Generates a random 16-byte per-key salt.</summary>
    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    /// <summary>Hashes <c>pepper || key</c> with the given salt and returns the PHC-encoded string.</summary>
    public static string Hash(string pepper, string key, byte[] salt) =>
        HashInternal(pepper, key, salt);

    /// <summary>Verifies <paramref name="key"/> against a stored verifier (constant-time).</summary>
    public static bool Verify(string encodedHash, string pepper, string key) =>
        Argon2.Verify(encodedHash, PasswordBytes(pepper, key));

    /// <summary>
    /// Verifies against a fixed dummy verifier with identical parameters so an unknown token costs
    /// the same as a real verification (no timing oracle). The result is always discarded.
    /// </summary>
    public static void BurnOnDummy(string pepper, string key) =>
        _ = Argon2.Verify(DummyHash.Value, PasswordBytes(pepper, key));

    private static string HashInternal(string pepper, string key, byte[] salt)
    {
        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing, // Argon2id
            Version = Argon2Version.Nineteen,
            TimeCost = TimeCost,
            MemoryCost = MemoryCostKiB,
            Lanes = Lanes,
            Threads = Lanes,
            Password = PasswordBytes(pepper, key),
            Salt = salt,
            HashLength = HashLengthBytes,
        };

        return Argon2.Hash(config);
    }

    private static byte[] PasswordBytes(string pepper, string key) =>
        Encoding.UTF8.GetBytes(pepper + key);
}
