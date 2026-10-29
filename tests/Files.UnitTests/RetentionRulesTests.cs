using Files.Application.Abstractions;
using Files.Application.Files;
using Files.Domain.Enums;
using FluentAssertions;

namespace Files.UnitTests;

public class RetentionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Days_within_default_resolve_to_a_finite_expiry()
    {
        var error = RetentionRules.ResolveFinite(30, null, legalHold: false, Now, out var retention);

        error.Should().BeNull();
        retention.Should().NotBeNull();
        retention!.Mode.Should().Be(RetentionMode.Finite);
        retention.Days.Should().Be(30);
        retention.ExpiresAt.Should().Be(Now.AddDays(30));
    }

    [Fact]
    public void Null_days_uses_the_default()
    {
        var error = RetentionRules.ResolveFinite(null, null, legalHold: false, Now, out var retention);

        error.Should().BeNull();
        retention!.Days.Should().Be(RetentionRules.DefaultDays);
    }

    [Fact]
    public void Extending_beyond_default_requires_a_reason_of_at_least_ten_chars()
    {
        var missing = RetentionRules.ResolveFinite(60, null, legalHold: false, Now, out _);
        missing.Should().NotBeNull();
        missing!.Code.Should().Be("retention_reason_required");
        missing.Status.Should().Be(422);

        var shortReason = RetentionRules.ResolveFinite(60, "need it", legalHold: false, Now, out _);
        shortReason!.Code.Should().Be("retention_reason_required");

        var ok = RetentionRules.ResolveFinite(60, "contract review until Q1", legalHold: false, Now, out var retention);
        ok.Should().BeNull();
        retention!.Days.Should().Be(60);
    }

    [Fact]
    public void Beyond_the_absolute_maximum_is_rejected()
    {
        var error = RetentionRules.ResolveFinite(400, null, legalHold: false, Now, out _);
        error.Should().NotBeNull();
        error!.Code.Should().Be("retention_exceeds_maximum");
        error.Status.Should().Be(422);
    }

    [Fact]
    public void Below_one_day_is_rejected()
    {
        var error = RetentionRules.ResolveFinite(0, null, legalHold: false, Now, out _);
        error!.Code.Should().Be("retention_exceeds_maximum");
    }

    [Fact]
    public void Legal_hold_blocks_retention_changes()
    {
        var error = RetentionRules.ResolveFinite(30, null, legalHold: true, Now, out _);
        error.Should().NotBeNull();
        error!.Code.Should().Be("legal_hold_active");
        error.Status.Should().Be(409);
    }

    [Fact]
    public void Failure_returns_no_retention()
    {
        var error = RetentionRules.ResolveFinite(400, null, legalHold: false, Now, out var retention);
        error.Should().NotBeNull();
        retention.Should().BeNull();
    }

    [Fact]
    public void Custom_default_and_max_are_honored()
    {
        var error = RetentionRules.ResolveFinite(20, null, legalHold: false, Now, out var retention, defaultDays: 15, maxDays: 30);
        error.Should().NotBeNull();
        error!.Code.Should().Be("retention_reason_required");
        retention.Should().BeNull();
    }

    [Fact]
    public void Indefinite_retention_is_rejected_for_humans()
    {
        var error = RetentionRules.RejectIndefiniteForHuman("indefinite");
        error.Should().NotBeNull();
        error!.Code.Should().Be("retention_not_allowed");
        error.Status.Should().Be(422);

        RetentionRules.RejectIndefiniteForHuman("finite").Should().BeNull();
        RetentionRules.RejectIndefiniteForHuman(null).Should().BeNull();
    }
}
