using Files.Application.Files;
using FluentAssertions;

namespace Files.UnitTests;

public class DownloadKeysTests
{
    [Fact]
    public void Generate_produces_an_8_char_alphanumeric_key()
    {
        var key = DownloadKeys.Generate();

        key.Should().HaveLength(DownloadKeys.KeyLength);
        key.Should().MatchRegex("^[A-Za-z0-9]{8}$");
    }

    [Fact]
    public void Generate_produces_distinct_keys()
    {
        var a = DownloadKeys.Generate();
        var b = DownloadKeys.Generate();

        a.Should().NotBe(b);
    }

    [Theory]
    [InlineData("Ab3xY9Qz")]
    [InlineData("00000000")]
    [InlineData("ZZZZZZZZ")]
    public void IsValidShape_accepts_valid_keys(string key)
    {
        DownloadKeys.IsValidShape(key).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("Ab3xY9Qz!")]
    [InlineData("Ab3xY9Qz12")]
    [InlineData("ab-3xY9")]
    public void IsValidShape_rejects_invalid_keys(string? key)
    {
        DownloadKeys.IsValidShape(key).Should().BeFalse();
    }

    [Fact]
    public void Hash_and_verify_round_trip_with_the_same_pepper_and_key()
    {
        const string pepper = "test-pepper";
        const string key = "Ab3xY9Qz";

        var hash = DownloadKeys.Hash(pepper, key, DownloadKeys.GenerateSalt());

        DownloadKeys.Verify(hash, pepper, key).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_a_wrong_key()
    {
        const string pepper = "test-pepper";
        var hash = DownloadKeys.Hash(pepper, "Ab3xY9Qz", DownloadKeys.GenerateSalt());

        DownloadKeys.Verify(hash, pepper, "WrongKey").Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_wrong_pepper()
    {
        const string key = "Ab3xY9Qz";
        var hash = DownloadKeys.Hash("pepper-a", key, DownloadKeys.GenerateSalt());

        DownloadKeys.Verify(hash, "pepper-b", key).Should().BeFalse();
    }

    [Fact]
    public void BurnOnDummy_completes_without_throwing()
    {
        var action = () => DownloadKeys.BurnOnDummy("test-pepper", "Ab3xY9Qz");
        action.Should().NotThrow();
    }
}
