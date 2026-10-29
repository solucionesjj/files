using Files.Application.Configuration;
using Files.Application.Files;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Files.UnitTests;

public class KeyThrottleTests
{
    private static KeyThrottle Default() => new(
        maxAttemptsPerWindow: 5,
        window: TimeSpan.FromMinutes(15),
        linkIpLockout: TimeSpan.FromMinutes(15),
        softLockThresholdPerHour: 100,
        softLock: TimeSpan.FromMinutes(15));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 500)]
    [InlineData(2, 1000)]
    [InlineData(3, 2000)]
    [InlineData(4, 4000)]
    [InlineData(5, 4000)]
    public void DelayForAttempt_applies_the_progressive_delay_capped_at_4s(int attempts, int expectedMs)
    {
        KeyThrottle.DelayForAttempt(attempts).Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void ShouldLockOutLinkIp_after_five_failures_in_the_window(int attempts, bool expected)
    {
        Default().ShouldLockOutLinkIp(attempts).Should().Be(expected);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    public void ShouldSoftLockLink_after_one_hundred_hourly_failures(int attempts, bool expected)
    {
        Default().ShouldSoftLockLink(attempts).Should().Be(expected);
    }

    [Fact]
    public void IsLockedOut_ignores_past_lockouts_and_honors_future_ones()
    {
        var now = new DateTimeOffset(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

        KeyThrottle.IsLockedOut(now.AddMinutes(-1), null, now).Should().BeFalse();
        KeyThrottle.IsLockedOut(null, now.AddMinutes(-1), now).Should().BeFalse();
        KeyThrottle.IsLockedOut(now.AddMinutes(1), null, now).Should().BeTrue();
        KeyThrottle.IsLockedOut(null, now.AddHours(1), now).Should().BeTrue();
    }

    [Fact]
    public void FromOptions_reads_the_canonical_abuse_defaults()
    {
        var config = new ConfigurationBuilder().Build();
        var options = LinksOptions.FromConfiguration(config);

        var throttle = KeyThrottle.FromOptions(options);

        throttle.MaxAttemptsPerWindow.Should().Be(5);
        throttle.Window.Should().Be(TimeSpan.FromMinutes(15));
        throttle.LinkIpLockout.Should().Be(TimeSpan.FromMinutes(15));
        throttle.SoftLockThresholdPerHour.Should().Be(100);
        throttle.SoftLock.Should().Be(TimeSpan.FromMinutes(15));
    }
}
