using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Files.IntegrationTests.Persistence;

[Collection(PersistenceCollectionDefinition.Name)]
public class FileRepositoryOwnerPredicateTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public FileRepositoryOwnerPredicateTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Get_owned_file_returns_only_the_owners_file_and_hides_foreign_files()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new FileRepository(db, TimeProvider.System);

        // Referential integrity: owners must exist before their files reference them.
        var ownerA = User.Create(Guid.NewGuid(), Guid.NewGuid(), "Owner A");
        var ownerB = User.Create(Guid.NewGuid(), Guid.NewGuid(), "Owner B");
        db.Users.Add(ownerA);
        db.Users.Add(ownerB);
        await db.SaveChangesAsync();

        var owned = FileEntity.CreateHuman(
            ownerA.Id, "a.txt", "files-local", "keys/a.txt", 10, "text/plain",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));

        var foreign = FileEntity.CreateHuman(
            ownerB.Id, "b.txt", "files-local", "keys/b.txt", 20, "text/plain",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));

        repository.Add(owned);
        repository.Add(foreign);
        await db.SaveChangesAsync();

        var result = await repository.GetOwnedFileAsync(owned.Id, ownerA.Id);
        result.Should().NotBeNull();
        result!.Id.Should().Be(owned.Id);

        // A foreign file must look like a 404 (null), never a 403: no existence oracle.
        var foreignResult = await repository.GetOwnedFileAsync(owned.Id, ownerB.Id);
        foreignResult.Should().BeNull();

        var missing = await repository.GetOwnedFileAsync(Guid.NewGuid(), ownerA.Id);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task Get_owned_integration_file_returns_only_the_integrations_file()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new FileRepository(db, TimeProvider.System);

        var owner = Integration.Create("acme-owner", "Acme Owner");
        var other = Integration.Create("acme-other", "Acme Other");
        db.Integrations.Add(owner);
        db.Integrations.Add(other);
        await db.SaveChangesAsync();

        var file = FileEntity.CreateIntegration(
            owner.Id, "data.csv", "files-local", "keys/data.csv", 100, "text/csv");

        repository.Add(file);
        await db.SaveChangesAsync();

        var result = await repository.GetOwnedIntegrationFileAsync(file.Id, owner.Id);
        result.Should().NotBeNull();
        result!.AccessMode.Should().Be(AccessMode.IntegrationOnly);

        var foreign = await repository.GetOwnedIntegrationFileAsync(file.Id, other.Id);
        foreign.Should().BeNull();
    }
}
