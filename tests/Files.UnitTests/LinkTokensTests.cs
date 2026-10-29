using Files.Application.Files;
using FluentAssertions;

namespace Files.UnitTests;

public class LinkTokensTests
{
    [Fact]
    public void Generate_produces_a_43_char_base64url_token()
    {
        var token = LinkTokens.Generate();
        token.Should().HaveLength(LinkTokens.TokenLength);
        token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
    }

    [Fact]
    public void Tokens_are_unique_and_decode_to_a_32_byte_hash()
    {
        var a = LinkTokens.Generate();
        var b = LinkTokens.Generate();
        a.Should().NotBe(b);

        LinkTokens.TryDecode(a, out var hashA).Should().BeTrue();
        LinkTokens.TryDecode(b, out var hashB).Should().BeTrue();

        hashA.Should().HaveCount(32); // SHA-256 output
        hashA.Should().NotEqual(hashB);
    }

    [Fact]
    public void Hash_matches_try_decode_for_the_same_token()
    {
        var token = LinkTokens.Generate();
        LinkTokens.TryDecode(token, out var hash);
        hash.Should().Equal(LinkTokens.Hash(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!")]
    public void TryDecode_rejects_invalid_tokens(string? token)
    {
        LinkTokens.TryDecode(token, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
    }

    [Fact]
    public void TryDecode_rejects_a_43_char_token_with_non_base64url_characters()
    {
        var token = new string('!', LinkTokens.TokenLength);
        LinkTokens.TryDecode(token, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
    }
}
