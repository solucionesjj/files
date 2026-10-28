using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Files.IntegrationTests.Persistence;

[Collection(PersistenceCollectionDefinition.Name)]
public class IntegrationRepositoryTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public IntegrationRepositoryTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Add_then_lookup_by_slug()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new IntegrationRepository(db);

        var integration = Integration.Create("acme-pipeline", "Acme Pipeline", "CI uploads");

        repository.Add(integration);
        await db.SaveChangesAsync();

        var bySlug = await repository.GetBySlugAsync("acme-pipeline");
        bySlug.Should().NotBeNull();
        bySlug!.Id.Should().Be(integration.Id);
        bySlug.Name.Should().Be("Acme Pipeline");
    }

    [Fact]
    public async Task Get_by_slug_returns_null_for_unknown_slug()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new IntegrationRepository(db);

        var loaded = await repository.GetBySlugAsync("does-not-exist");

        loaded.Should().BeNull();
    }
}
