using Files.Application.Integrations;
using FluentAssertions;

namespace Files.UnitTests;

public class IdempotencyKeysTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData("abcdefgh", true)]
    public void IsValid_enforces_the_8_128_character_window(string? key, bool expected)
    {
        IdempotencyKeys.IsValid(key).Should().Be(expected);
    }

    [Fact]
    public void IsValid_accepts_a_128_character_key_and_rejects_129()
    {
        IdempotencyKeys.IsValid(new string('a', 128)).Should().BeTrue();
        IdempotencyKeys.IsValid(new string('a', 129)).Should().BeFalse();
    }

    [Fact]
    public void Hash_is_stable_hex_and_distinguishes_method_path_and_body()
    {
        var a = IdempotencyKeys.Hash("POST", "/v1/integrations/upload-sessions", []);
        var b = IdempotencyKeys.Hash("POST", "/v1/integrations/upload-sessions", []);

        a.Should().Be(b);
        a.Should().MatchRegex("^[a-f0-9]{64}$");

        IdempotencyKeys.Hash("POST", "/v1/integrations/upload-sessions", "{}"u8)
            .Should().NotBe(IdempotencyKeys.Hash("POST", "/v1/integrations/upload-sessions", "{\"x\":1}"u8));
        IdempotencyKeys.Hash("GET", "/v1/integrations/upload-sessions", [])
            .Should().NotBe(IdempotencyKeys.Hash("POST", "/v1/integrations/upload-sessions", []));
    }

    [Fact]
    public void Retention_is_24_hours()
    {
        IdempotencyKeys.Retention.Should().Be(TimeSpan.FromHours(24));
    }
}
