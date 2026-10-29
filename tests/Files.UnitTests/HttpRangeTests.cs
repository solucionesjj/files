using Files.Application.Integrations;
using FluentAssertions;

namespace Files.UnitTests;

public class HttpRangeTests
{
    [Fact]
    public void No_header_is_not_requested()
    {
        HttpRange.Parse(null, 100, out _, out _).Should().Be(ByteRangeParse.NotRequested);
        HttpRange.Parse("", 100, out _, out _).Should().Be(ByteRangeParse.NotRequested);
        HttpRange.Parse("   ", 100, out _, out _).Should().Be(ByteRangeParse.NotRequested);
    }

    [Fact]
    public void Bounded_range_is_parsed_and_clamped_to_total_length()
    {
        var result = HttpRange.Parse("bytes=10-19", 100, out var offset, out var length);

        result.Should().Be(ByteRangeParse.Satisfiable);
        offset.Should().Be(10);
        length.Should().Be(10);

        HttpRange.Parse("bytes=90-199", 100, out offset, out length);
        offset.Should().Be(90);
        length.Should().Be(10);
    }

    [Fact]
    public void Open_ended_and_suffix_ranges_are_supported()
    {
        HttpRange.Parse("bytes=50-", 100, out var offset, out var length);
        offset.Should().Be(50);
        length.Should().Be(50);

        HttpRange.Parse("bytes=-20", 100, out offset, out length);
        offset.Should().Be(80);
        length.Should().Be(20);
    }

    [Theory]
    [InlineData("bytes=100-", 100)]
    [InlineData("bytes=200-", 100)]
    public void Unsatisfiable_ranges_return_416(string header, long total)
    {
        HttpRange.Parse(header, total, out _, out _).Should().Be(ByteRangeParse.Unsatisfiable);
    }

    [Theory]
    [InlineData("items=0-9")]
    [InlineData("bytes=5-2")]
    [InlineData("bytes=0-1,3-4")]
    [InlineData("bytes=")]
    [InlineData("bytes=abc")]
    [InlineData("bytes=-")]
    [InlineData("bytes=-0")]
    public void Malformed_or_multi_ranges_are_ignored(string header)
    {
        HttpRange.Parse(header, 100, out _, out _).Should().Be(ByteRangeParse.Ignore);
    }
}
