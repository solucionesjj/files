using Files.Application.Deletion;
using FluentAssertions;

namespace Files.UnitTests;

public class DeletionBackoffTests
{
    [Fact]
    public void Base_delay_doubles_per_attempt_and_caps_at_one_hour()
    {
        DeletionBackoff.BaseDelay(1).Should().Be(TimeSpan.FromMinutes(1));
        DeletionBackoff.BaseDelay(2).Should().Be(TimeSpan.FromMinutes(2));
        DeletionBackoff.BaseDelay(3).Should().Be(TimeSpan.FromMinutes(4));
        DeletionBackoff.BaseDelay(4).Should().Be(TimeSpan.FromMinutes(8));
        DeletionBackoff.BaseDelay(5).Should().Be(TimeSpan.FromMinutes(16));
        DeletionBackoff.BaseDelay(6).Should().Be(TimeSpan.FromMinutes(32));
        // 2^6 = 64 m exceeds the 1 h cap.
        DeletionBackoff.BaseDelay(7).Should().Be(TimeSpan.FromHours(1));
        DeletionBackoff.BaseDelay(100).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Jittered_delay_stays_within_twenty_percent_of_the_capped_base()
    {
        // A fixed seed keeps the assertion deterministic while still exercising the jitter branch.
        var random = new Random(12345);

        foreach (var attempt in new[] { 1, 2, 3, 6, 10 })
        {
            var baseDelay = DeletionBackoff.BaseDelay(attempt);
            var lower = (long)(baseDelay.Ticks * 0.8);
            var upper = (long)(baseDelay.Ticks * 1.2);

            for (var i = 0; i < 200; i++)
            {
                var jittered = DeletionBackoff.BackoffDelay(attempt, random);
                jittered.Ticks.Should().BeInRange(lower, upper,
                    $"attempt {attempt} must stay within ±20% of {baseDelay}");
            }
        }
    }

    [Fact]
    public void Jittered_delay_is_never_negative()
    {
        // Random.NextDouble() in [0,1) ⇒ factor in [0.8, 1.2); the clamp guards against any
        // negative tick product from future changes.
        var random = new Random(0);
        for (var i = 0; i < 1000; i++)
        {
            DeletionBackoff.BackoffDelay(1, random).Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
    }
}
