using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Identity;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// End-to-end auth/authorization tests against the real API host (self-signed test JWTs + the
/// shared PostgreSQL 18 fixture): JIT provisioning, roles read from <c>users.role</c> (ADR-0014),
/// 401/403/404 semantics, admin integration CRUD, and Files-issued API keys.
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class AuthApiTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Me_provisions_the_user_jit_and_returns_identity()
    {
        var objectId = Guid.NewGuid();
        var token = AuthTestJwt.CreateUserToken(objectId, name: "Jane JIT", email: "jane@example.com");

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        me.GetProperty("tenantId").GetGuid().Should().Be(AuthTestJwt.TenantId);
        me.GetProperty("displayName").GetString().Should().Be("Jane JIT");
        // Role comes from users.role (JIT creates "user"), never from the token.
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("user");

        (await PersistenceTestHelpers.CountWhereAsync(fixture.ConnectionString, "users", "object_id", objectId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Bootstrap_admin_email_promotes_the_first_admin()
    {
        Environment.SetEnvironmentVariable("Auth__BootstrapAdminEmail", "firstadmin@example.com");
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                AuthTestJwt.CreateUserToken(Guid.NewGuid(), name: "First Admin", email: "firstadmin@example.com"));

            var me = await client.GetAsync("/v1/me");
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray()
                .Select(r => r.GetString()).Should().Contain("admin");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__BootstrapAdminEmail", null);
        }
    }

    [Fact]
    public async Task Admin_endpoint_returns_403_for_a_user_and_200_for_an_admin()
    {
        await using var factory = CreateFactory();

        using var userClient = factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AuthTestJwt.CreateUserToken(Guid.NewGuid()));
        var forbidden = await userClient.GetAsync("/v1/admin/integrations");

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("forbidden");

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var slug = "dup-" + Guid.NewGuid().ToString("N")[..8];
        var body = new { slug, name = "Duplicate", permissions = new[] { "file.read" } };

        (await client.PostAsJsonAsync("/v1/admin/integrations", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicate = await client.PostAsJsonAsync("/v1/admin/integrations", body);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("integration_slug_taken");
    }

    [Fact]
    public async Task Admin_promotes_a_user_and_the_change_applies_to_the_next_request()
    {
        await using var factory = CreateFactory();

        // JIT creates a plain user.
        var userObjectId = Guid.NewGuid();
        using (var userClient = factory.CreateClient())
        {
            userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                AuthTestJwt.CreateUserToken(userObjectId, name: "Promotee", email: "promotee@example.com"));
            var me = await userClient.GetAsync("/v1/me");
            me.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var userId = await FindUserIdAsync(userObjectId);

        using (var adminClient = factory.CreateClient())
        {
            adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
            var promote = await adminClient.PatchAsJsonAsync($"/v1/admin/users/{userId}/role", new { role = "admin" });
            promote.StatusCode.Should().Be(HttpStatusCode.OK);
            (await promote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString().Should().Be("admin");
        }

        (await PersistenceTestHelpers.CountWhereAsync(fixture.ConnectionString, "users", "role", "admin"))
            .Should().BeGreaterThan(0);

        // The promoted user can now reach an admin endpoint.
        using (var promotedClient = factory.CreateClient())
        {
            promotedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                AuthTestJwt.CreateUserToken(userObjectId));
            var ok = await promotedClient.GetAsync("/v1/admin/integrations");
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Admin_issues_an_api_key_an_integration_uses_it_and_revocation_denies_it()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var slug = "key-test-" + Guid.NewGuid().ToString("N")[..8];
        var create = await adminClient.PostAsJsonAsync("/v1/admin/integrations", new
        {
            slug,
            name = "Key Test",
            permissions = new[] { "file.read" },
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var integrationId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var issue = await adminClient.PostAsJsonAsync($"/v1/admin/integrations/{integrationId}/api-keys", new
        {
            name = "primary",
            scopes = new[] { "file.read" },
        });
        issue.StatusCode.Should().Be(HttpStatusCode.Created);
        var issued = await issue.Content.ReadFromJsonAsync<JsonElement>();
        var keyId = issued.GetProperty("id").GetGuid();
        var apiKey = issued.GetProperty("apiKey").GetString();
        apiKey.Should().NotBeNullOrEmpty();

        using (var integrationClient = factory.CreateClient())
        {
            integrationClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            var me = await integrationClient.GetAsync("/v1/integrations/me");
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await me.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("integrationId").GetGuid().Should().Be(integrationId);
            body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain("file.read");
        }

        var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/v1/admin/integrations/{integrationId}/api-keys/{keyId}")
        {
            Content = JsonContent.Create(new { reason = "credential rotated" }),
        };
        revoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
        var revokeResponse = await factory.CreateClient().SendAsync(revoke);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var integrationClient = factory.CreateClient())
        {
            integrationClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            var me = await integrationClient.GetAsync("/v1/integrations/me");
            me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private async Task<string> AdminTokenAsync()
    {
        var objectId = Guid.NewGuid();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var admin = User.Create(AuthTestJwt.TenantId, objectId, "Files Admin", "admin@example.com");
        admin.Role = UserRole.Admin;
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return AuthTestJwt.CreateUserToken(objectId);
    }

    private async Task<Guid> FindUserIdAsync(Guid objectId)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var user = await db.Users.SingleAsync(u => u.ObjectId == objectId);
        return user.Id;
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
