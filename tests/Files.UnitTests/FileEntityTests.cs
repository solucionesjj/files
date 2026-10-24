using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using FluentAssertions;

namespace Files.UnitTests;

public class FileEntityTests
{
    [Fact]
    public void Create_human_sets_owner_and_applies_retention()
    {
        var owner = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var file = FileEntity.CreateHuman(
            owner, "report.pdf", "files-local", "keys/report.pdf", 512, "application/pdf",
            Retention.Finite(expiresAt, 30));

        file.OwnerType.Should().Be(OwnerType.Human);
        file.OwnerUserId.Should().Be(owner);
        file.OwnerIntegrationId.Should().BeNull();
        file.AccessMode.Should().Be(AccessMode.OwnerOnly);
        file.RetentionMode.Should().Be(RetentionMode.Finite);
        file.ExpiresAt.Should().Be(expiresAt);
        file.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void Create_integration_is_indefinite_and_integration_only()
    {
        var integration = Guid.NewGuid();

        var file = FileEntity.CreateIntegration(
            integration, "data.csv", "files-local", "keys/data.csv", 100, "text/csv");

        file.OwnerType.Should().Be(OwnerType.Integration);
        file.OwnerIntegrationId.Should().Be(integration);
        file.OwnerUserId.Should().BeNull();
        file.AccessMode.Should().Be(AccessMode.IntegrationOnly);
        file.RetentionMode.Should().Be(RetentionMode.Indefinite);
        file.ExpiresAt.Should().BeNull();
        file.RetentionDays.Should().BeNull();
    }
}
