using Files.Application.Notifications;
using FluentAssertions;

namespace Files.UnitTests;

public class NotificationRetryTests
{
    [Fact]
    public void Backoff_grows_per_attempt_and_caps_at_six_hours()
    {
        NotificationRetry.BackoffDelay(1).Should().Be(TimeSpan.FromMinutes(1));
        NotificationRetry.BackoffDelay(2).Should().Be(TimeSpan.FromMinutes(5));
        NotificationRetry.BackoffDelay(3).Should().Be(TimeSpan.FromMinutes(15));
        NotificationRetry.BackoffDelay(4).Should().Be(TimeSpan.FromHours(1));
        NotificationRetry.BackoffDelay(5).Should().Be(TimeSpan.FromHours(6));
        NotificationRetry.BackoffDelay(6).Should().Be(TimeSpan.FromHours(6));
        NotificationRetry.BackoffDelay(100).Should().Be(TimeSpan.FromHours(6));
    }
}
