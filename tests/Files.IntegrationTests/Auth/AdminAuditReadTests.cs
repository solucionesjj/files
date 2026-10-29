using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Read-only admin/auditor surface (FR-ADM-005/006/008, FR-AUD-001..003): audit events, download
/// events, usage, and settings. Admins and auditors can read these; a plain user cannot, and no
/// admin operation returns file content (D-19).
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class AdminAuditReadTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task User_gets_403_and_admin_and_auditor_get_200()
    {
        await using var factory = CreateFactory();

        using (var userClient = factory.CreateClient())
        {
            userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                AuthTestJwt.CreateUserToken(Guid.NewGuid()));
            foreach (var path in ReadPaths)
            {
                var forbidden = await userClient.GetAsync(path);
                forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
                (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("forbidden");
            }
        }

        using (var auditorClient = factory.CreateClient())
        {
            auditorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AuditorTokenAsync());
            foreach (var path in ReadPaths)
            {
                (await auditorClient.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
            }
        }

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
        foreach (var path in ReadPaths)
        {
            (await adminClient.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Audit_events_return_file_and_admin_rows_with_filters_and_cursor()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await client.GetAsync($"/v1/admin/audit-events?action={seeded.Action}&limit=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        body.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        body.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

        var categories = items.Select(i => i.GetProperty("category").GetString()).OrderBy(c => c).ToList();
        categories.Should().Equal(["admin", "file"]);

        var fileEvent = items.Single(i => i.GetProperty("category").GetString() == "file");
        fileEvent.GetProperty("fileId").GetGuid().Should().Be(seeded.File1Id);
        fileEvent.GetProperty("actorType").GetString().Should().Be("system");

        var adminEvent = items.Single(i => i.GetProperty("category").GetString() == "admin");
        // An admin event targeting a file surfaces its target_id as fileId for cross-category filtering.
        adminEvent.GetProperty("fileId").GetGuid().Should().Be(seeded.File1Id);
    }

    [Fact]
    public async Task Audit_events_reject_invalid_filter()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await client.GetAsync("/v1/admin/audit-events?category=bogus");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_query_parameter");
    }

    [Fact]
    public async Task Download_events_filter_by_file_and_result()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await client.GetAsync($"/v1/admin/download-events?fileId={seeded.File1Id}&result=completed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1);
        items[0].GetProperty("source").GetString().Should().Be("public_link");
        items[0].GetProperty("result").GetString().Should().Be("completed");
        items[0].GetProperty("bytesSent").GetInt64().Should().Be(1234);
    }

    [Fact]
    public async Task Usage_returns_storage_files_downloads_and_integrations()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await client.GetAsync("/v1/admin/usage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("generatedAt").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("storage").GetProperty("totalBytes").GetInt64().Should().BeGreaterThanOrEqualTo(350);
        body.GetProperty("storage").GetProperty("byOwnerType").GetProperty("integration").GetInt64().Should().BeGreaterThanOrEqualTo(350);
        body.GetProperty("files").GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("files").GetProperty("available").GetInt64().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("downloads").GetProperty("last7Days").GetInt64().Should().BeGreaterThanOrEqualTo(1234);

        var integration = body.GetProperty("integrations").EnumerateArray()
            .SingleOrDefault(i => i.GetProperty("integrationId").GetGuid() == seeded.IntegrationId);
        integration.Should().NotBeNull();
        integration!.GetProperty("files").GetInt64().Should().Be(2);
        integration.GetProperty("bytes").GetInt64().Should().Be(350);
    }

    [Fact]
    public async Task Settings_return_platform_settings_without_content()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await client.GetAsync("/v1/admin/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var settings = body.GetProperty("settings");
        settings.GetProperty("public_links_enabled").GetBoolean().Should().BeTrue();
        settings.GetProperty("max_file_size_bytes").GetInt64().Should().Be(5368709120);
        // No storage key/bucket/secret may leak through the settings surface.
        body.ToString().Should().NotContain("SecretAccessKey").And.NotContain("storage_key");
    }

    private static readonly string[] ReadPaths =
    [
        "/v1/admin/audit-events",
        "/v1/admin/download-events",
        "/v1/admin/usage",
        "/v1/admin/settings",
    ];

    private sealed record SeedResult(Guid IntegrationId, Guid File1Id, Guid File2Id, string Action);

    private async Task<SeedResult> SeedAsync()
    {
        var integrationId = Guid.NewGuid();
        var file1Id = Guid.NewGuid();
        var file2Id = Guid.NewGuid();
        var action = "audit-read-test-" + Guid.NewGuid().ToString("N")[..12];

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO integrations (id, slug, name, status) VALUES (@id, @slug, @name, 'active')";
            command.Parameters.AddWithValue("id", integrationId);
            command.Parameters.AddWithValue("slug", "audit-it-" + Guid.NewGuid().ToString("N")[..8]);
            command.Parameters.AddWithValue("name", "Audit Read Test");
            await command.ExecuteNonQueryAsync();
        }

        foreach (var (id, size) in new[] { (file1Id, 100L), (file2Id, 250L) })
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO files (id, owner_type, owner_integration_id, original_filename,
                                   storage_bucket, storage_key, size_bytes, content_type, status,
                                   access_mode, retention_mode, scan_status, metadata)
                VALUES (@id, 'integration', @integrationId, 'test.bin',
                        'ecs-files-production', @storageKey, @size, 'application/octet-stream',
                        'available', 'integration_only', 'indefinite', 'clean', '{}')
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("integrationId", integrationId);
            command.Parameters.AddWithValue("storageKey", $"f/{id}");
            command.Parameters.AddWithValue("size", size);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO download_events (file_id, actor_type, source, result, bytes_sent)
                VALUES (@fileId, 'anonymous', 'public_link', 'completed', 1234)
                """;
            command.Parameters.AddWithValue("fileId", file1Id);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO file_audit_events (file_id, actor_type, action, result, metadata) VALUES (@fileId, 'system', @action, 'success', '{}')";
            command.Parameters.AddWithValue("fileId", file1Id);
            command.Parameters.AddWithValue("action", action);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO admin_audit_events (actor_user_id, actor_role, action, target_type, target_id, result, metadata) VALUES (NULL, 'admin', @action, 'file', @fileId, 'success', '{}')";
            command.Parameters.AddWithValue("action", action);
            command.Parameters.AddWithValue("fileId", file1Id);
            await command.ExecuteNonQueryAsync();
        }

        return new SeedResult(integrationId, file1Id, file2Id, action);
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

    private async Task<string> AuditorTokenAsync()
    {
        var objectId = Guid.NewGuid();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var auditor = User.Create(AuthTestJwt.TenantId, objectId, "Files Auditor", "auditor@example.com");
        auditor.Role = UserRole.Auditor;
        db.Users.Add(auditor);
        await db.SaveChangesAsync();
        return AuthTestJwt.CreateUserToken(objectId);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", fixture.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", "/tmp/files-staging");
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "integration-test-pepper");
        Environment.SetEnvironmentVariable("Entra__TenantId", AuthTestJwt.TenantId.ToString());
        Environment.SetEnvironmentVariable("Entra__ApiAudience", AuthTestJwt.Audience);
        Environment.SetEnvironmentVariable("Entra__ApiClientId", AuthTestJwt.ApiClientId.ToString());
        Environment.SetEnvironmentVariable("Entra__IssuerSigningKey", AuthTestJwt.PublicKeyBase64);
        return new WebApplicationFactory<Program>();
    }
}
