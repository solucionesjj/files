using Files.Api.Authorization;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Identity;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Repositories;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Exercises <see cref="IntegrationRequirementHandler"/> against a real database: the machine
/// caller must map azp/appid to an active integration and have the required permissions granted in
/// <c>integration_permissions</c> (never from token scopes).
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class IntegrationRequirementHandlerTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Grants_access_when_azp_maps_to_an_active_integration_with_the_permission()
    {
        await using var db = CreateDb();
        var clientId = Guid.NewGuid();
        await SeedAsync(db, clientId, active: true, IntegrationPermissions.FileRead);

        var handler = new IntegrationRequirementHandler(new IntegrationRepository(db));
        var context = CreateContext(
            [new IntegrationRequirement(IntegrationPermissions.FileRead)],
            AuthTestJwt.AppPrincipal(clientId, PortalRoles.Integration));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_when_the_required_permission_is_not_granted_in_the_database()
    {
        await using var db = CreateDb();
        var clientId = Guid.NewGuid();
        await SeedAsync(db, clientId, active: true, IntegrationPermissions.FileRead);

        var handler = new IntegrationRequirementHandler(new IntegrationRepository(db));
        var context = CreateContext(
            [new IntegrationRequirement(IntegrationPermissions.FileWrite)],
            AuthTestJwt.AppPrincipal(clientId, PortalRoles.Integration));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_when_the_integration_is_disabled()
    {
        await using var db = CreateDb();
        var clientId = Guid.NewGuid();
        await SeedAsync(db, clientId, active: false, IntegrationPermissions.FileRead);

        var handler = new IntegrationRequirementHandler(new IntegrationRepository(db));
        var context = CreateContext(
            [new IntegrationRequirement(IntegrationPermissions.FileRead)],
            AuthTestJwt.AppPrincipal(clientId, PortalRoles.Integration));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_when_the_azp_is_unregistered()
    {
        await using var db = CreateDb();
        var handler = new IntegrationRequirementHandler(new IntegrationRepository(db));
        var context = CreateContext(
            [new IntegrationRequirement(IntegrationPermissions.FileRead)],
            AuthTestJwt.AppPrincipal(Guid.NewGuid(), PortalRoles.Integration));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_when_the_app_role_is_missing()
    {
        await using var db = CreateDb();
        var clientId = Guid.NewGuid();
        await SeedAsync(db, clientId, active: true, IntegrationPermissions.FileRead);

        var handler = new IntegrationRequirementHandler(new IntegrationRepository(db));
        var context = CreateContext(
            [new IntegrationRequirement(IntegrationPermissions.FileRead)],
            AuthTestJwt.AppPrincipal(clientId));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static AuthorizationHandlerContext CreateContext(
        IReadOnlyList<IAuthorizationRequirement> requirements,
        System.Security.Claims.ClaimsPrincipal principal) =>
        new(requirements, principal, resource: null);

    private FilesDbContext CreateDb() =>
        new(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));

    private static async Task SeedAsync(
        FilesDbContext db,
        Guid clientId,
        bool active,
        params string[] permissions)
    {
        var integration = Integration.Create(
            "test-integration-" + Guid.NewGuid().ToString("N")[..8],
            "Test Integration");
        integration.EntraClientId = clientId;
        integration.Status = active ? IntegrationStatus.Active : IntegrationStatus.Disabled;
        if (!active)
        {
            integration.DisabledAt = DateTimeOffset.UtcNow;
            integration.DisabledReason = "test";
        }

        foreach (var permission in permissions)
        {
            integration.Permissions.Add(new IntegrationPermissionEntry
            {
                IntegrationId = integration.Id,
                Permission = permission,
            });
        }

        db.Integrations.Add(integration);
        await db.SaveChangesAsync();
    }
}
