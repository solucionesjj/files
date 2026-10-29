using Files.Application.Integrations;
using FluentAssertions;

namespace Files.UnitTests;

public class IntegrationQuotaTests
{
    [Theory]
    [InlineData(100, 100, false)]
    [InlineData(101, 100, true)]
    [InlineData(0, 100, false)]
    public void ExceedsMaxFileSize_compares_strictly(long sizeBytes, long max, bool expected)
    {
        IntegrationQuota.ExceedsMaxFileSize(sizeBytes, max).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 5, false)]
    [InlineData(4, 5, false)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, true)]
    public void ExceedsSessionLimit_is_saturated_at_the_limit(int open, int max, bool expected)
    {
        IntegrationQuota.ExceedsSessionLimit(open, max).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 100, 200, false)]
    [InlineData(100, 100, 200, false)]
    [InlineData(101, 100, 200, true)]
    [InlineData(200, 1, 200, true)]
    public void WouldExceed_is_overflow_safe(long used, long additional, long quota, bool expected)
    {
        IntegrationQuota.WouldExceed(used, additional, quota).Should().Be(expected);
    }

    [Fact]
    public void WouldExceed_ignores_zero_additional_bytes()
    {
        IntegrationQuota.WouldExceed(long.MaxValue, 0, 1).Should().BeFalse();
    }
}
