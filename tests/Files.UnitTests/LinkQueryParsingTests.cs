using Files.Application.Files;
using Files.Domain.Enums;
using FluentAssertions;

namespace Files.UnitTests;

public class LinkQueryParsingTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("active", LinkStatus.Active)]
    [InlineData("revoked", LinkStatus.Revoked)]
    [InlineData("expired", LinkStatus.Expired)]
    public void TryParseStatus_maps_known_and_empty_values(string? value, LinkStatus? expected)
    {
        LinkQueryParsing.TryParseStatus(value, out var status).Should().BeTrue();
        status.Should().Be(expected);
    }

    [Fact]
    public void TryParseStatus_rejects_unknown_values()
    {
        LinkQueryParsing.TryParseStatus("bogus", out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_round_trips_through_encode_and_decode()
    {
        var cursor = new LinkCursor(new DateTimeOffset(2026, 10, 28, 20, 0, 0, TimeSpan.Zero), Guid.NewGuid());

        var encoded = LinkQueryParsing.EncodeCursor(cursor);
        LinkQueryParsing.TryDecodeCursor(encoded, out var decoded).Should().BeTrue();

        decoded.Should().Be(cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDecodeCursor_returns_true_for_empty(string? value)
    {
        LinkQueryParsing.TryDecodeCursor(value, out var cursor).Should().BeTrue();
        cursor.Should().BeNull();
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("!!!!")]
    public void TryDecodeCursor_rejects_malformed_cursors(string value)
    {
        LinkQueryParsing.TryDecodeCursor(value, out var cursor).Should().BeFalse();
        cursor.Should().BeNull();
    }
}
