using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// S7 portal file management (rename/tag/retention/delete) against the real API host + PostgreSQL 18:
/// If-Match concurrency, retention validation, legal hold, tag persistence/replacement, owner
/// isolation, and logical deletion with link revocation.
/// </summary>
[Collection(AuthCollectionDefinition.Name)]
public class PortalFileManagementTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Patch_renames_file_and_returns_a_new_etag()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "before.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new { fileName = "after.txt" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Read(response);
        body.GetProperty("fileName").GetString().Should().Be("after.txt");
        body.GetProperty("version").GetInt32().Should().Be(file.Version + 1);

        var detail = await Read(await client.GetAsync($"/v1/portal/files/{file.Id}"));
        detail.GetProperty("fileName").GetString().Should().Be("after.txt");
    }

    [Fact]
    public async Task Patch_replaces_tags_case_insensitively_and_persists_them()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "tagged.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        // Mixed case + duplicate: deduped to a single "Finance" tag.
        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new { tags = new[] { "Finance", "finance", "contracts" } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Read(response);
        Tags(body).Should().BeEquivalentTo("Finance", "contracts");

        var detail = await Read(await client.GetAsync($"/v1/portal/files/{file.Id}"));
        Tags(detail).Should().BeEquivalentTo("Finance", "contracts");

        // Replacing the whole set drops the previous tags.
        var second = await PatchAsync(client, file.Id, $"\"{file.Version + 1}\"", new { tags = new[] { "legal" } });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        Tags(await Read(second)).Should().Equal("legal");

        // The existing tag was reused (no duplicate rows) and is case-insensitive.
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        (await db.Tags.CountAsync(t => t.Name == "finance" || t.Name == "Finance")).Should().Be(1);
    }

    [Fact]
    public async Task Patch_changes_retention_and_records_the_reason()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "retain.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new
        {
            retention = new { mode = "finite", days = 60, reason = "contract review until Q1" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Read(response);
        body.GetProperty("retention").GetProperty("days").GetInt32().Should().Be(60);
        body.GetProperty("retention").GetProperty("reason").GetString().Should().Be("contract review until Q1");
        body.GetProperty("retention").GetProperty("expiresAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("indefinite", null, "retention_not_allowed")]
    [InlineData("finite", 400, "retention_exceeds_maximum")]
    [InlineData("finite", 0, "retention_exceeds_maximum")]
    public async Task Patch_rejects_invalid_retention(string mode, int? days, string expectedCode)
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "retain.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new
        {
            retention = new { mode, days },
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Read(response)).GetProperty("code").GetString().Should().Be(expectedCode);
    }

    [Fact]
    public async Task Patch_extends_retention_beyond_default_requires_a_reason()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "retain.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new
        {
            retention = new { mode = "finite", days = 60 },
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Read(response)).GetProperty("code").GetString().Should().Be("retention_reason_required");
    }

    [Fact]
    public async Task Patch_requires_a_matching_If_Match_header()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "concurrent.txt", FileStatus.Available);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        // Missing If-Match is rejected.
        var missing = await PatchAsync(client, file.Id, null, new { fileName = "x.txt" });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A stale version is rejected with 412 stale_version.
        var stale = await PatchAsync(client, file.Id, $"\"{file.Version + 5}\"", new { fileName = "x.txt" });
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await Read(stale)).GetProperty("code").GetString().Should().Be("stale_version");
    }

    [Fact]
    public async Task Patch_retention_change_is_blocked_by_legal_hold()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "held.txt", FileStatus.Available, legalHold: true);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await PatchAsync(client, file.Id, $"\"{file.Version}\"", new
        {
            retention = new { mode = "finite", days = 90, reason = "trying to change a held file" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Read(response)).GetProperty("code").GetString().Should().Be("legal_hold_active");
    }

    [Fact]
    public async Task Patch_and_delete_foreign_or_integration_files_return_404()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var (_, foreignObjectId) = await SeedOwnerAsync();
        var mine = await SeedFileAsync(ownerId, "mine.txt", FileStatus.Available);

        Guid foreignId;
        Guid integrationId;
        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString)))
        {
            var foreignOwnerId = (await db.Users.SingleAsync(u => u.ObjectId == foreignObjectId)).Id;
            var foreign = FileEntity.CreateHuman(foreignOwnerId, "foreign.txt", "files-local", "keys/" + Guid.NewGuid(), 128, "text/plain",
                Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
            foreign.Status = FileStatus.Available;
            db.Files.Add(foreign);

            var integration = Integration.Create("mgmt-int-" + Guid.NewGuid().ToString("N")[..6], "Mgmt Int");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            var integrationFile = FileEntity.CreateIntegration(integration.Id, "int.csv", "files-local", "keys/" + Guid.NewGuid(), 10, "text/csv");
            db.Files.Add(integrationFile);
            await db.SaveChangesAsync();

            foreignId = foreign.Id;
            integrationId = integrationFile.Id;
        }

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        foreach (var id in new[] { foreignId, integrationId, Guid.NewGuid() })
        {
            (await PatchAsync(client, id, "\"1\"", new { fileName = "x.txt" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.DeleteAsync($"/v1/portal/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // Positive control: the owner can still patch/delete their own file.
        (await PatchAsync(client, mine.Id, $"\"{mine.Version}\"", new { fileName = "mine-renamed.txt" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_marks_pending_deletion_and_revokes_active_links()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "delete.txt", FileStatus.Available);
        var linkId = await SeedActiveLinkAsync(ownerId, file.Id);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await client.DeleteAsync($"/v1/portal/files/{file.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var deleted = await db.Files.SingleAsync(f => f.Id == file.Id);
        deleted.Status.Should().Be(FileStatus.PendingDeletion);
        deleted.DeletedAt.Should().BeNull(); // pending_deletion ≠ deleted (ck_files_deleted)
        deleted.NextDeletionAt.Should().NotBeNull();
        deleted.DeletionRequestedByType.Should().Be(DeletionRequesterType.User);
        deleted.AccessMode.Should().Be(AccessMode.OwnerOnly);

        var link = await db.DownloadLinks.SingleAsync(l => l.Id == linkId);
        link.Status.Should().Be(LinkStatus.Revoked);
        link.RevokedAt.Should().NotBeNull();

        // A deleted file can no longer issue a download grant.
        var grant = await client.PostAsync($"/v1/portal/files/{file.Id}/downloads", null);
        grant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(grant)).GetProperty("code").GetString().Should().Be("file_unavailable");
    }

    [Fact]
    public async Task Delete_is_blocked_by_legal_hold()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedFileAsync(ownerId, "held.txt", FileStatus.Available, legalHold: true);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var response = await client.DeleteAsync($"/v1/portal/files/{file.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Read(response)).GetProperty("code").GetString().Should().Be("legal_hold_active");
    }

    [Fact]
    public async Task Post_downloads_rejects_unavailable_and_integration_files()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var processing = await SeedFileAsync(ownerId, "processing.txt", FileStatus.Processing);

        Guid integrationFileId;
        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString)))
        {
            var integration = Integration.Create("dl-int-" + Guid.NewGuid().ToString("N")[..6], "Dl Int");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            var integrationFile = FileEntity.CreateIntegration(integration.Id, "int.csv", "files-local", "keys/" + Guid.NewGuid(), 10, "text/csv");
            db.Files.Add(integrationFile);
            await db.SaveChangesAsync();
            integrationFileId = integrationFile.Id;
        }

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var processingGrant = await client.PostAsync($"/v1/portal/files/{processing.Id}/downloads", null);
        processingGrant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(processingGrant)).GetProperty("code").GetString().Should().Be("file_unavailable");

        var integrationGrant = await client.PostAsync($"/v1/portal/files/{integrationFileId}/downloads", null);
        integrationGrant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(integrationGrant)).GetProperty("code").GetString().Should().Be("file_not_found");
    }

    // ------------------------------------------------------------------ helpers

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid objectId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AuthTestJwt.CreateUserToken(objectId));
        return client;
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid fileId, string? ifMatch, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/v1/portal/files/{fileId}")
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }

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
        bool legalHold = false)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerId, name, "files-local", "keys/" + Guid.NewGuid(), 128, "text/plain",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
        file.Status = status;
        file.ScanStatus = status == FileStatus.Available ? ScanStatus.Clean : ScanStatus.NotScanned;
        if (legalHold)
        {
            file.LegalHold = true;
            file.LegalHoldSetAt = DateTimeOffset.UtcNow;
        }

        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file;
    }

    private async Task<Guid> SeedActiveLinkAsync(Guid ownerId, Guid fileId)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(fixture.ConnectionString));
        var link = new DownloadLink
        {
            FileId = fileId,
            TokenHash = SHA256.HashData(Guid.NewGuid().ToByteArray()),
            AccessMode = LinkAccessMode.Private,
            AllowAllTenant = false,
            Status = LinkStatus.Active,
            CreatedByType = LinkCreatorType.User,
            CreatedBy = ownerId,
        };
        db.DownloadLinks.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task ResetAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM download_events; DELETE FROM download_grants; DELETE FROM download_links; DELETE FROM file_tags; DELETE FROM files; DELETE FROM tags;";
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

    private static string[] Tags(JsonElement file) =>
        file.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();
}
