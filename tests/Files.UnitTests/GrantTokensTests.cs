using Files.Application.Files;
using FluentAssertions;

namespace Files.UnitTests;

public class GrantTokensTests
{
    [Fact]
    public void Generate_produces_a_43_char_base64url_token()
    {
        var token = GrantTokens.Generate();
        token.Should().HaveLength(GrantTokens.TokenLength);
        token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
    }

    [Fact]
    public void Tokens_are_unique_and_decode_to_32_bytes()
    {
        var a = GrantTokens.Generate();
        var b = GrantTokens.Generate();
        a.Should().NotBe(b);

        GrantTokens.TryDecode(a, out var hashA).Should().BeTrue();
        GrantTokens.TryDecode(b, out var hashB).Should().BeTrue();

        hashA.Should().HaveCount(32); // SHA-256 output
        hashA.Should().NotEqual(hashB);
    }

    [Fact]
    public void Hash_matches_try_decode_for_the_same_token()
    {
        var token = GrantTokens.Generate();
        GrantTokens.TryDecode(token, out var hash);
        hash.Should().Equal(GrantTokens.Hash(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!")]
    public void TryDecode_rejects_invalid_tokens(string? token)
    {
        GrantTokens.TryDecode(token, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
    }

    [Fact]
    public void TryDecode_rejects_a_43_char_token_with_non_base64url_characters()
    {
        var token = new string('!', GrantTokens.TokenLength);
        GrantTokens.TryDecode(token, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
    }
}
