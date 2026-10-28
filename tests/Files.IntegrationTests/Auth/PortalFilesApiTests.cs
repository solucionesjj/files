using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Owner-scoped portal file list/detail (S6) against the real API host and PostgreSQL 18: search,
/// status filter, sort, cursor pagination, expiry range, tag filter, and uniform 404 for foreign
/// or integration-owned files (no existence oracle, ADR-0012).
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class PortalFilesApiTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task List_returns_owned_files_newest_first_and_paginates_by_cursor()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();

        // Insert newest-last so "newest first" order is deterministic (now() advances per commit).
        await SeedFileAsync(ownerId, "first.txt", FileStatus.Available);
        await SeedFileAsync(ownerId, "second.txt", FileStatus.Available);
        await SeedFileAsync(ownerId, "third.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var page1 = await ListAsync(client, "limit=2");
        page1.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        page1.GetProperty("items").GetArrayLength().Should().Be(2);
        Names(page1).Should().Equal("third.txt", "second.txt");

        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        var page2 = await ListAsync(client, $"limit=2&cursor={Uri.EscapeDataString(cursor)}");
        page2.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        Names(page2).Should().Equal("first.txt");
        page2.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task List_search_matches_partial_filename_case_insensitively()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        await SeedFileAsync(ownerId, "Quarterly-Report.pdf", FileStatus.Available);
        await SeedFileAsync(ownerId, "notes.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "q=quarterly")).Should().Equal("Quarterly-Report.pdf");
        Names(await ListAsync(client, "q=REPORT")).Should().Equal("Quarterly-Report.pdf");
        Names(await ListAsync(client, "q=zzz")).Should().BeEmpty();
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        await SeedFileAsync(ownerId, "available.txt", FileStatus.Available);
        await SeedFileAsync(ownerId, "quarantined.txt", FileStatus.Quarantined);
        await SeedFileAsync(ownerId, "deleted.txt", FileStatus.Deleted, deleted: true);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "status=available")).Should().Equal("available.txt");
        Names(await ListAsync(client, "status=quarantined&status=deleted"))
            .Should().BeEquivalentTo("quarantined.txt", "deleted.txt");
    }

    [Fact]
    public async Task List_sorts_by_name_and_downloads()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        await SeedFileAsync(ownerId, "banana.txt", FileStatus.Available, downloads: 10);
        await SeedFileAsync(ownerId, "apple.txt", FileStatus.Available, downloads: 5);
        await SeedFileAsync(ownerId, "cherry.txt", FileStatus.Available, downloads: 3);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "sort=name")).Should().Equal("apple.txt", "banana.txt", "cherry.txt");
        Names(await ListAsync(client, "sort=-name")).Should().Equal("cherry.txt", "banana.txt", "apple.txt");
        Names(await ListAsync(client, "sort=downloads")).Should().Equal("cherry.txt", "apple.txt", "banana.txt");
        Names(await ListAsync(client, "sort=-downloads")).Should().Equal("banana.txt", "apple.txt", "cherry.txt");
    }

    [Fact]
    public async Task List_filters_by_expiry_range()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        await SeedFileAsync(ownerId, "soon.txt", FileStatus.Available,
            expiresAt: DateTimeOffset.UtcNow.AddDays(5));
        await SeedFileAsync(ownerId, "later.txt", FileStatus.Available,
            expiresAt: DateTimeOffset.UtcNow.AddDays(100));

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "expiringWithinDays=10")).Should().Equal("soon.txt");
        Names(await ListAsync(client, $"expiresBefore={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(50).ToString("O"))}"))
            .Should().Equal("soon.txt");
        Names(await ListAsync(client, $"expiresAfter={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(50).ToString("O"))}"))
            .Should().Equal("later.txt");
    }

    [Fact]
    public async Task List_filters_by_tag()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var tagged = await SeedFileAsync(ownerId, "tagged.txt", FileStatus.Available);
        await SeedFileAsync(ownerId, "untagged.txt", FileStatus.Available);

        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString)))
        {
            var tag = new Tag { Scope = TagScope.Global, Name = "finance" };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.FileTags.Add(new FileTag { FileId = tagged.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "tag=finance")).Should().Equal("tagged.txt");
        Names(await ListAsync(client, "tag=none")).Should().BeEmpty();
    }

    [Fact]
    public async Task List_never_exposes_integration_or_foreign_files()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var (_, foreignObjectId) = await SeedOwnerAsync();
        await SeedFileAsync(ownerId, "mine.txt", FileStatus.Available);

        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString)))
        {
            var integration = Integration.Create("list-int-" + Guid.NewGuid().ToString("N")[..6], "List Int");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            db.Files.Add(FileEntity.CreateIntegration(
                integration.Id, "integration.csv", "files-local", "keys/" + Guid.NewGuid(), 100, "text/csv"));
            await db.SaveChangesAsync();

            var foreign = FileEntity.CreateHuman(
                (await db.Users.SingleAsync(u => u.ObjectId == foreignObjectId)).Id,
                "theirs.txt", "files-local", "keys/" + Guid.NewGuid(), 128, "text/plain",
                Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
            foreign.Status = FileStatus.Available;
            db.Files.Add(foreign);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        Names(await ListAsync(client, "")).Should().Equal("mine.txt");
    }

    [Fact]
    public async Task Get_returns_owned_file_metadata_with_etag()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "detail.txt", FileStatus.Available, downloads: 7);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await client.GetAsync($"/v1/portal/files/{file.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();

        var body = await Read(response);
        body.GetProperty("id").GetGuid().Should().Be(file.Id);
        body.GetProperty("fileName").GetString().Should().Be("detail.txt");
        body.GetProperty("status").GetString().Should().Be("available");
        body.GetProperty("downloads").GetProperty("count").GetInt64().Should().Be(7);
    }

    [Fact]
    public async Task Get_foreign_or_integration_file_returns_404_file_not_found()
    {
        await ResetFilesAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var (_, foreignObjectId) = await SeedOwnerAsync();
        var mine = await SeedFileAsync(ownerId, "mine.txt", FileStatus.Available);

        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString)))
        {
            var foreignOwnerId = (await db.Users.SingleAsync(u => u.ObjectId == foreignObjectId)).Id;
            var foreign = FileEntity.CreateHuman(
                foreignOwnerId, "foreign.txt", "files-local", "keys/" + Guid.NewGuid(), 128, "text/plain",
                Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
            foreign.Status = FileStatus.Available;
            db.Files.Add(foreign);
            await db.SaveChangesAsync();

            var integration = Integration.Create("get-int-" + Guid.NewGuid().ToString("N")[..6], "Get Int");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            var integrationFile = FileEntity.CreateIntegration(
                integration.Id, "int.csv", "files-local", "keys/" + Guid.NewGuid(), 10, "text/csv");
            db.Files.Add(integrationFile);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        // Positive control: the owner can read their own file.
        (await client.GetAsync($"/v1/portal/files/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignId = await FindForeignFileIdAsync();
        var integrationId = await FindIntegrationFileIdAsync();

        foreach (var id in new[] { foreignId, integrationId, Guid.NewGuid() })
        {
            var response = await client.GetAsync($"/v1/portal/files/{id}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Read(response)).GetProperty("code").GetString().Should().Be("file_not_found");
        }
    }

    [Fact]
    public async Task List_rejects_invalid_sort_status_and_cursor()
    {
        await ResetFilesAsync();
        await SeedOwnerAsync();

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, Guid.NewGuid());

        (await ListStatusAsync(client, "sort=bogus")).Should().Be(HttpStatusCode.BadRequest);
        (await ListStatusAsync(client, "status=bogus")).Should().Be(HttpStatusCode.BadRequest);
        (await ListStatusAsync(client, "limit=101")).Should().Be(HttpStatusCode.BadRequest);
        (await ListStatusAsync(client, "expiringWithinDays=0")).Should().Be(HttpStatusCode.BadRequest);
        (await ListStatusAsync(client, "cursor=%21%21%21")).Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ helpers

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid objectId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AuthTestJwt.CreateUserToken(objectId));
        return client;
    }

    private static async Task<HttpResponseMessage> ListRawAsync(HttpClient client, string query) =>
        await client.GetAsync($"/v1/portal/files?{query}");

    private static async Task<JsonElement> ListAsync(HttpClient client, string query) =>
        await Read(await ListRawAsync(client, query));

    private static async Task<HttpStatusCode> ListStatusAsync(HttpClient client, string query) =>
        (await ListRawAsync(client, query)).StatusCode;

    private async Task<(Guid UserId, Guid ObjectId)> SeedOwnerAsync()
    {
        var objectId = Guid.NewGuid();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var owner = User.Create(AuthTestJwt.TenantId, objectId, "Owner", "owner@example.com");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        return (owner.Id, objectId);
    }

    private async Task<FileEntity> SeedFileAsync(
        Guid ownerId,
        string name,
        FileStatus status,
        bool deleted = false,
        long downloads = 0,
        DateTimeOffset? expiresAt = null)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var retention = expiresAt is null
            ? Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30)
            : Retention.Finite(expiresAt.Value, 30);

        var file = FileEntity.CreateHuman(ownerId, name, "files-local", "keys/" + Guid.NewGuid(), 128, "text/plain", retention);
        file.Status = status;
        file.ScanStatus = status == FileStatus.Available ? ScanStatus.Clean : ScanStatus.NotScanned;
        file.SuccessfulDownloads = downloads;
        if (deleted)
        {
            file.DeletedAt = DateTimeOffset.UtcNow;
        }

        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file;
    }

    private async Task<Guid> FindForeignFileIdAsync()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        return (await db.Files.SingleAsync(f => f.OriginalFilename == "foreign.txt")).Id;
    }

    private async Task<Guid> FindIntegrationFileIdAsync()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        return (await db.Files.SingleAsync(f => f.OriginalFilename == "int.csv")).Id;
    }

    private async Task ResetFilesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM file_tags; DELETE FROM files; DELETE FROM tags;";
        await command.ExecuteNonQueryAsync();
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

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("fileName").GetString()!).ToArray();
}
