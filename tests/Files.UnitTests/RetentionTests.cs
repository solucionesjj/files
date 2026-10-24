using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using FluentAssertions;

namespace Files.UnitTests;

public class RetentionTests
{
    [Fact]
    public void Finite_requires_an_expiry_and_stores_the_day_count()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var retention = Retention.Finite(expiresAt, 30);

        retention.Mode.Should().Be(RetentionMode.Finite);
        retention.IsFinite.Should().BeTrue();
        retention.ExpiresAt.Should().Be(expiresAt);
        retention.Days.Should().Be(30);
    }

    [Fact]
    public void Finite_rejects_a_zero_or_negative_day_count()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var act = () => Retention.Finite(expiresAt, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Finite_rejects_more_than_the_absolute_maximum_of_365_days()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(400);

        var act = () => Retention.Finite(expiresAt, 366);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Indefinite_has_no_expiry_and_no_day_count()
    {
        var retention = Retention.Indefinite();

        retention.Mode.Should().Be(RetentionMode.Indefinite);
        retention.IsFinite.Should().BeFalse();
        retention.ExpiresAt.Should().BeNull();
        retention.Days.Should().BeNull();
    }
}
