using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Files.IntegrationTests.Persistence;

[Collection(PersistenceCollectionDefinition.Name)]
public class UserRepositoryTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public UserRepositoryTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Add_then_get_by_tenant_identity_returns_the_user_with_db_defaults()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new UserRepository(db);

        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = User.Create(tenantId, objectId, "Jane Doe", "jane@example.com");

        repository.Add(user);
        await db.SaveChangesAsync();

        var loaded = await repository.GetByTenantIdentityAsync(tenantId, objectId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id, "the application-generated UUIDv7 must be persisted");
        loaded.DisplayName.Should().Be("Jane Doe");
        loaded.Email.Should().Be("jane@example.com");
        loaded.Status.Should().Be(UserStatus.Active);
        loaded.Version.Should().Be(1);
        loaded.CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-2), "created_at comes from the database now()");
    }

    [Fact]
    public async Task Get_by_tenant_identity_returns_null_for_unknown_identity()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var repository = new UserRepository(db);

        var loaded = await repository.GetByTenantIdentityAsync(Guid.NewGuid(), Guid.NewGuid());

        loaded.Should().BeNull();
    }
}
