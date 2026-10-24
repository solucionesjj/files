using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Domain.Identity;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// End-to-end auth/authorization tests against the real API host (self-signed test JWTs + the
/// shared PostgreSQL 18 fixture): JIT provisioning, roles→policy, 401/403/404 semantics, and admin
/// integration CRUD with audit + outbox.
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class AuthApiTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Me_provisions_the_user_jit_and_returns_identity()
    {
        var objectId = Guid.NewGuid();
        var token = AuthTestJwt.CreateUserToken(objectId, name: "Jane JIT", email: "jane@example.com", roles: [PortalRoles.User]);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        me.GetProperty("tenantId").GetGuid().Should().Be(AuthTestJwt.TenantId);
        me.GetProperty("displayName").GetString().Should().Be("Jane JIT");
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Files.User");

        (await PersistenceTestHelpers.CountWhereAsync(fixture.ConnectionString, "users", "object_id", objectId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Admin_endpoint_returns_403_for_a_user_and_200_for_an_admin()
    {
        await using var factory = CreateFactory();

        using var userClient = factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateUserToken(Guid.NewGuid(), roles: [PortalRoles.User]));
        var forbidden = await userClient.GetAsync("/v1/admin/integrations");

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("forbidden");

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateAdminToken(Guid.NewGuid()));
        var ok = await adminClient.GetAsync("/v1/admin/integrations");

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_token_returns_401_invalid_token()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/admin/integrations");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_token");
    }

    [Fact]
    public async Task Unknown_integration_returns_404_not_403_for_an_admin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateAdminToken(Guid.NewGuid()));

        var response = await client.GetAsync($"/v1/admin/integrations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("integration_not_found");
    }

    [Fact]
    public async Task App_only_token_is_rejected_on_user_endpoints()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateAppToken(Guid.NewGuid(), PortalRoles.Integration));

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_create_disable_and_replace_permissions_with_audit_and_outbox()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateAdminToken(Guid.NewGuid()));

        var slug = "ci-pipeline-" + Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync("/v1/admin/integrations", new
        {
            slug,
            name = "CI Pipeline",
            description = "uploads builds",
            permissions = new[] { "file.write", "file.read" },
            allowedIpCidrs = new[] { "10.0.0.0/8" },
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be("active");
        created.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(["file.read", "file.write"]);

        var badDisable = await client.PatchAsJsonAsync($"/v1/admin/integrations/{id}", new { status = "disabled" });
        badDisable.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await badDisable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("reason_required");

        var disable = await client.PatchAsJsonAsync($"/v1/admin/integrations/{id}", new { status = "disabled", reason = "security review pending" });
        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await disable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("disabled");

        var permissions = await client.PutAsJsonAsync($"/v1/admin/integrations/{id}/permissions", new { permissions = new[] { "file.delete" } });
        permissions.StatusCode.Should().Be(HttpStatusCode.OK);
        (await permissions.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()).Should().Equal(["file.delete"]);

        (await PersistenceTestHelpers.CountWhereAsync(fixture.ConnectionString, "admin_audit_events", "target_id", id))
            .Should().BeGreaterThan(0);
        (await PersistenceTestHelpers.CountWhereAsync(fixture.ConnectionString, "outbox_messages", "aggregate_id", id))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Duplicate_slug_returns_409_integration_slug_taken()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateAdminToken(Guid.NewGuid()));

        var slug = "dup-" + Guid.NewGuid().ToString("N")[..8];
        var body = new { slug, name = "Duplicate", permissions = new[] { "file.read" } };

        (await client.PostAsJsonAsync("/v1/admin/integrations", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicate = await client.PostAsJsonAsync("/v1/admin/integrations", body);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("integration_slug_taken");
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", fixture.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", "/tmp/files-staging");
        Environment.SetEnvironmentVariable("Entra__TenantId", AuthTestJwt.TenantId.ToString());
        Environment.SetEnvironmentVariable("Entra__ApiAudience", AuthTestJwt.Audience);
        Environment.SetEnvironmentVariable("Entra__ApiClientId", AuthTestJwt.ApiClientId.ToString());
        Environment.SetEnvironmentVariable("Entra__IssuerSigningKey", AuthTestJwt.PublicKeyBase64);
        return new WebApplicationFactory<Program>();
    }
}
