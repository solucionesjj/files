using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class ChecksumsTests
{
    [Fact]
    public void Is_hex_sha256_accepts_exactly_64_hex_characters()
    {
        Checksums.IsHexSha256(new string('a', 64)).Should().BeTrue();
        Checksums.IsHexSha256(new string('A', 64)).Should().BeTrue();
        Checksums.IsHexSha256(new string('a', 63)).Should().BeFalse();
        Checksums.IsHexSha256(new string('g', 64)).Should().BeFalse();
    }

    [Fact]
    public void Normalize_lowercases_and_blanks_to_null()
    {
        Checksums.Normalize("ABCDEF").Should().Be("abcdef");
        Checksums.Normalize(null).Should().BeNull();
        Checksums.Normalize("  ").Should().BeNull();
    }

    [Fact]
    public void Compute_hex_matches_the_known_empty_sha256()
    {
        // SHA-256 of the empty message.
        Checksums.ComputeHex([]).Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task Compute_hex_async_matches_the_byte_computation()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("files");
        using var stream = new MemoryStream(bytes);

        (await Checksums.ComputeHexAsync(stream)).Should().Be(Checksums.ComputeHex(bytes));
    }
}
