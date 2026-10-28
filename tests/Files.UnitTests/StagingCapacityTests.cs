using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class StagingCapacityTests
{
    [Fact]
    public void Allows_when_free_minus_reserve_covers_the_allocation()
    {
        StagingCapacity.Allows(freeBytes: 100, reserveBytes: 10, additionalBytes: 5).Should().BeTrue();
    }

    [Fact]
    public void Allows_exactly_when_the_usable_capacity_equals_the_allocation()
    {
        StagingCapacity.Allows(freeBytes: 15, reserveBytes: 10, additionalBytes: 5).Should().BeTrue();
    }

    [Fact]
    public void Rejects_when_the_reserve_would_be_breached()
    {
        StagingCapacity.Allows(freeBytes: 14, reserveBytes: 10, additionalBytes: 5).Should().BeFalse();
    }

    [Fact]
    public void A_zero_or_negative_allocation_never_breaches_the_reserve()
    {
        StagingCapacity.Allows(freeBytes: 1, reserveBytes: 10, additionalBytes: 0).Should().BeTrue();
        StagingCapacity.Allows(freeBytes: 1, reserveBytes: 10, additionalBytes: -5).Should().BeTrue();
    }

    [Theory]
    [InlineData(100, 10, 90)]
    [InlineData(5, 10, 0)]
    public void Usable_capacity_is_free_minus_reserve_floored_at_zero(long free, long reserve, long expected)
    {
        StagingCapacity.Usable(free, reserve).Should().Be(expected);
    }
}
