using Files.Application.Upload;
using FluentAssertions;

namespace Files.UnitTests;

public class ScanBackoffTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 960)]
    [InlineData(7, 1800)]
    [InlineData(50, 1800)]
    public void DelayFor_is_exponential_and_capped(int attempts, int expectedSeconds)
    {
        ScanBackoff.DelayFor(attempts).TotalSeconds.Should().Be(expectedSeconds);
    }
}
