using Files.Application.Notifications;
using FluentAssertions;

namespace Files.UnitTests;

public class NotificationTimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Due_at_is_expiry_minus_interval_days()
    {
        var expiresAt = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

        NotificationTiming.DueAt(expiresAt, 7).Should().Be(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero));
        NotificationTiming.DueAt(expiresAt, 3).Should().Be(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero));
        NotificationTiming.DueAt(expiresAt, 1).Should().Be(new DateTimeOffset(2026, 11, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Retroactive_intervals_are_not_scheduled()
    {
        var expiresInTwoDays = Now.AddDays(2);

        // 7-day reminder is already past for a file that expires in 2 days → retroactive.
        NotificationTiming.IsRetroactive(expiresInTwoDays, 7, Now).Should().BeTrue();
        // 3-day reminder is past too.
        NotificationTiming.IsRetroactive(expiresInTwoDays, 3, Now).Should().BeTrue();
        // 1-day reminder is still in the future.
        NotificationTiming.IsRetroactive(expiresInTwoDays, 1, Now).Should().BeFalse();
    }

    [Fact]
    public void Has_any_future_due_is_true_only_when_some_interval_is_future()
    {
        NotificationTiming.HasAnyFutureDue(Now.AddDays(2), [7, 3, 1], Now).Should().BeTrue();
        NotificationTiming.HasAnyFutureDue(Now.AddHours(1), [7, 3, 1], Now).Should().BeFalse();
        NotificationTiming.HasAnyFutureDue(Now.AddDays(30), [7, 3, 1], Now).Should().BeTrue();
    }
}
