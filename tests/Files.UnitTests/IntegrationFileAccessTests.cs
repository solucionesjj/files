using Files.Application.Integrations;
using Files.Domain.Entities;
using FluentAssertions;

namespace Files.UnitTests;

public class IntegrationFileAccessTests
{
    private static readonly Guid IntegrationA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid IntegrationB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void Owner_can_read_without_a_grant()
    {
        IntegrationFileAccess.CanRead(IntegrationA, IntegrationA, hasActiveGrant: false).Should().BeTrue();
    }

    [Fact]
    public void Non_owner_can_read_only_with_an_active_grant()
    {
        IntegrationFileAccess.CanRead(IntegrationA, IntegrationB, hasActiveGrant: false).Should().BeFalse();
        IntegrationFileAccess.CanRead(IntegrationA, IntegrationB, hasActiveGrant: true).Should().BeTrue();
    }

    [Fact]
    public void A_revoked_or_expired_grant_is_inactive()
    {
        var now = DateTimeOffset.UtcNow;
        var revoked = new IntegrationFileGrant { RevokedAt = now };
        revoked.IsActive(now).Should().BeFalse();

        var expired = new IntegrationFileGrant { ExpiresAt = now.AddMinutes(-1) };
        expired.IsActive(now).Should().BeFalse();

        var active = new IntegrationFileGrant { ExpiresAt = now.AddMinutes(1) };
        active.IsActive(now).Should().BeTrue();

        var perpetual = new IntegrationFileGrant();
        perpetual.IsActive(now).Should().BeTrue();
    }
}
