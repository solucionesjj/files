using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Files.Application.Files;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Auth;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// S8 public links and downloads against the real API host + PostgreSQL 18 + LocalStack S3:
/// create/list/revoke/stats, key-shown-once, redemption single-use, generic 404s, reservation
/// concurrency, completion-only counting, owner isolation, integration files, and the
/// <c>public_links_enabled</c> policy.
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class PublicLinkTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    private const string Pepper = "integration-test-pepper";

    [Fact]
    public async Task Create_returns_url_token_and_key_once_and_lists_hide_them()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var created = await client.PostAsJsonAsync($"/v1/portal/files/{file.Id}/links", new { accessMode = "public" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await Read(created);
        body.GetProperty("url").GetString().Should().Be($"/d/{body.GetProperty("token").GetString()}");
        body.GetProperty("token").GetString().Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        body.GetProperty("downloadKey").GetString().Should().MatchRegex("^[A-Za-z0-9]{8}$");
        body.GetProperty("status").GetString().Should().Be("active");

        // The server-assigned expiry is ~15 days out (admin TTL, owner cannot override).
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        expiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(15), TimeSpan.FromMinutes(2));

        // Neither list endpoint returns the token or the key.
        var fileLinks = await Read(await client.GetAsync($"/v1/portal/files/{file.Id}/links"));
        var linkItem = fileLinks.GetProperty("items")[0];
        linkItem.TryGetProperty("token", out _).Should().BeFalse();
        linkItem.TryGetProperty("downloadKey", out _).Should().BeFalse();

        var myLinks = await Read(await client.GetAsync("/v1/portal/links"));
        var myItem = myLinks.GetProperty("items")[0];
        myItem.TryGetProperty("token", out _).Should().BeFalse();
        myItem.TryGetProperty("downloadKey", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_public_link_returns_non_sensitive_metadata()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz", maxDownloads: 5);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/public/download-links/{token}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Read(response);
        body.GetProperty("fileName").GetString().Should().Be("share.txt");
        body.GetProperty("sizeBytes").GetInt64().Should().Be(file.SizeBytes);
        body.GetProperty("requiresKey").GetBoolean().Should().BeTrue();
        body.GetProperty("requiresAuthentication").GetBoolean().Should().BeFalse();
        body.GetProperty("remainingDownloads").GetInt32().Should().Be(5);
        body.GetProperty("ownerDisplayName").GetString().Should().Be("Owner");
    }

    [Fact]
    public async Task Redeem_correct_key_issues_grant_streams_once_and_counts_on_completion()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var content = new byte[256];
        Random.Shared.NextBytes(content);
        var file = await SeedAvailableFileAsync(ownerId, "share.bin", content);
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var redeem = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "Ab3xY9Qz" });
        redeem.StatusCode.Should().Be(HttpStatusCode.Created);
        var grant = await Read(redeem);
        var grantUrl = grant.GetProperty("downloadUrl").GetString()!;

        // Reserved but not yet counted before streaming.
        await AssertLinkCountersAsync(file.Id, successful: 0, reserved: 1);

        var download = await anonymous.GetAsync(grantUrl);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);

        // Completed: reservation converted to success; file counter bumped once.
        await AssertLinkCountersAsync(file.Id, successful: 1, reserved: 0);
        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString)))
        {
            (await db.Files.SingleAsync(f => f.Id == file.Id)).SuccessfulDownloads.Should().Be(1);
        }

        // The grant is single-use.
        var reuse = await anonymous.GetAsync(grantUrl);
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Read(reuse)).GetProperty("code").GetString().Should().Be("grant_consumed");
    }

    [Fact]
    public async Task Redeem_wrong_key_returns_a_generic_404()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var redeem = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "WrongKey" });
        redeem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(redeem)).GetProperty("code").GetString().Should().Be("link_not_found");
    }

    [Fact]
    public async Task Redeem_expired_or_revoked_link_returns_404()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");

        var (expiredToken, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var (revokedToken, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz", status: LinkStatus.Revoked);

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var expired = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{expiredToken}/downloads", new { key = "Ab3xY9Qz" });
        expired.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(expired)).GetProperty("code").GetString().Should().Be("link_not_found");

        var revoked = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{revokedToken}/downloads", new { key = "Ab3xY9Qz" });
        revoked.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(revoked)).GetProperty("code").GetString().Should().Be("link_not_found");
    }

    [Fact]
    public async Task Redeem_when_max_downloads_reached_returns_404()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz", maxDownloads: 1, successful: 1);

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var redeem = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "Ab3xY9Qz" });
        redeem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(redeem)).GetProperty("code").GetString().Should().Be("link_not_found");
    }

    [Fact]
    public async Task Reservation_enforces_max_downloads_under_concurrency()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz", maxDownloads: 2);

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var attempts = Enumerable.Range(0, 3)
            .Select(_ => anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "Ab3xY9Qz" }))
            .ToArray();

        var responses = await Task.WhenAll(attempts);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(2);
        responses.Count(r => r.StatusCode == HttpStatusCode.NotFound).Should().Be(1);

        await AssertLinkCountersAsync(file.Id, successful: 0, reserved: 2);
    }

    [Fact]
    public async Task Revoke_invalidates_outstanding_grants_and_returns_204()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var content = new byte[64];
        var file = await SeedAvailableFileAsync(ownerId, "share.bin", content);
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var client = ClientFor(factory, objectId);

        // Issue an unused grant before revoking.
        var redeem = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "Ab3xY9Qz" });
        redeem.StatusCode.Should().Be(HttpStatusCode.Created);
        var grantUrl = (await Read(redeem)).GetProperty("downloadUrl").GetString()!;

        var linkId = await GetLinkIdAsync(file.Id);

        var revoke = await client.DeleteAsync($"/v1/portal/links/{linkId}?reason=leaked");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The outstanding grant is now invalid, and its reservation was released.
        var stream = await anonymous.GetAsync(grantUrl);
        stream.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AssertLinkCountersAsync(file.Id, successful: 0, reserved: 0);
    }

    [Fact]
    public async Task Stats_reflect_completed_downloads_failed_attempts_and_recent_events()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, _) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var client = ClientFor(factory, objectId);

        await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "WrongKey" });

        var linkId = await GetLinkIdAsync(file.Id);
        var stats = await client.GetAsync($"/v1/portal/links/{linkId}/stats");
        stats.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Read(stats);
        body.GetProperty("linkId").GetString().Should().Be(linkId.ToString());
        body.GetProperty("successfulDownloads").GetInt32().Should().Be(0);
        body.GetProperty("failedAttempts").GetInt32().Should().Be(1);
        body.GetProperty("recentEvents").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Foreign_link_returns_404_for_revoke_and_stats()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var (otherId, otherObjectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (_, linkId) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, otherObjectId);

        var revoke = await client.DeleteAsync($"/v1/portal/links/{linkId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var stats = await client.GetAsync($"/v1/portal/links/{linkId}/stats");
        stats.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Integration_file_cannot_get_a_link()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var integrationFile = await SeedIntegrationFileAsync();

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);

        var created = await client.PostAsJsonAsync($"/v1/portal/files/{integrationFile.Id}/links", new { accessMode = "public" });
        created.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(created)).GetProperty("code").GetString().Should().Be("file_not_found");
    }

    [Fact]
    public async Task Public_links_disabled_rejects_creation_with_409()
    {
        await ResetAsync();
        await SetPlatformSettingAsync("public_links_enabled", "false");
        try
        {
            var (ownerId, objectId) = await SeedOwnerAsync();
            var file = await SeedAvailableFileAsync(ownerId, "share.txt");

            await using var factory = CreateFactory();
            using var client = ClientFor(factory, objectId);

            var created = await client.PostAsJsonAsync($"/v1/portal/files/{file.Id}/links", new { accessMode = "public" });
            created.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await Read(created)).GetProperty("code").GetString().Should().Be("public_links_disabled");
        }
        finally
        {
            await SetPlatformSettingAsync("public_links_enabled", "true");
        }
    }

    [Fact]
    public async Task Soft_locked_link_returns_429_too_many_attempts()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "share.txt");
        var (token, linkId) = await SeedLinkAsync(ownerId, file.Id, "Ab3xY9Qz");

        await SeedLockoutAsync(linkId, DateTimeOffset.UtcNow.AddHours(1));

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var redeem = await anonymous.PostAsJsonAsync($"/v1/public/download-links/{token}/downloads", new { key = "Ab3xY9Qz" });
        redeem.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Read(redeem)).GetProperty("code").GetString().Should().Be("too_many_attempts");
    }

    // ------------------------------------------------------------------ helpers

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid objectId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AuthTestJwt.CreateUserToken(objectId));
        return client;
    }

    private async Task<(Guid UserId, Guid ObjectId)> SeedOwnerAsync()
    {
        var objectId = Guid.NewGuid();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var owner = User.Create(AuthTestJwt.TenantId, objectId, "Owner", "owner@example.com");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        return (owner.Id, objectId);
    }

    private async Task<FileEntity> SeedAvailableFileAsync(Guid ownerId, string name, byte[]? content = null)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerId, name, LocalStackS3Fixture.Bucket, string.Empty, content?.Length ?? 16, "application/octet-stream",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
        file.StorageKey = $"f/{file.Id}";
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        db.Files.Add(file);
        await db.SaveChangesAsync();

        if (content is not null)
        {
            await PutObjectAsync(file.StorageKey, content);
        }

        return file;
    }

    private async Task<FileEntity> SeedIntegrationFileAsync()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var integration = Integration.Create($"it-{Guid.NewGuid():N}"[..12], "Integration");
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var file = FileEntity.CreateIntegration(
            integration.Id, "integration.bin", LocalStackS3Fixture.Bucket, $"i/{Guid.NewGuid()}", 16, "application/octet-stream");
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file;
    }

    /// <summary>Seeds a public link with a known token/key and returns (token, linkId).</summary>
    private async Task<(string Token, Guid LinkId)> SeedLinkAsync(
        Guid ownerId,
        Guid fileId,
        string key,
        LinkStatus status = LinkStatus.Active,
        DateTimeOffset? expiresAt = null,
        int? maxDownloads = null,
        int successful = 0,
        int reserved = 0)
    {
        var token = LinkTokens.Generate();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var link = new DownloadLink
        {
            FileId = fileId,
            TokenHash = LinkTokens.Hash(token),
            AccessMode = LinkAccessMode.Public,
            AllowAllTenant = false,
            DownloadKeyHash = DownloadKeys.Hash(Pepper, key, DownloadKeys.GenerateSalt()),
            DownloadKeyVersion = 1,
            Status = status,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(15),
            MaxDownloads = maxDownloads,
            SuccessfulDownloads = successful,
            ReservedDownloads = reserved,
            CreatedByType = LinkCreatorType.User,
            CreatedBy = ownerId,
            RevokedAt = status == LinkStatus.Revoked ? DateTimeOffset.UtcNow : null,
        };
        db.DownloadLinks.Add(link);
        await db.SaveChangesAsync();
        return (token, link.Id);
    }

    private async Task<Guid> GetLinkIdAsync(Guid fileId)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        return (await db.DownloadLinks.SingleAsync(l => l.FileId == fileId)).Id;
    }

    private async Task AssertLinkCountersAsync(Guid fileId, int successful, int reserved)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var link = await db.DownloadLinks.SingleAsync(l => l.FileId == fileId);
        link.SuccessfulDownloads.Should().Be(successful);
        link.ReservedDownloads.Should().Be(reserved);
    }

    private async Task SeedLockoutAsync(Guid linkId, DateTimeOffset lockedUntil)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO abuse_lockouts (scope_type, scope_key, locked_until, reason) VALUES ('link', @key, @until, 'soft_lock')";
        command.Parameters.AddWithValue("key", linkId.ToString());
        command.Parameters.AddWithValue("until", lockedUntil);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetPlatformSettingAsync(string key, string value)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE platform_settings SET value = to_jsonb(@value) WHERE key = @key";
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync();
    }

    private async Task PutObjectAsync(string key, byte[] content)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            UseHttp = true,
            AuthenticationRegion = LocalStackS3Fixture.Region,
            ServiceURL = localstack.ServiceUrl,
        };
        using var client = new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);
        using var stream = new MemoryStream(content);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = LocalStackS3Fixture.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = "application/octet-stream",
        });
    }

    private async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM download_events; DELETE FROM download_key_attempts; DELETE FROM abuse_lockouts; DELETE FROM download_grants; DELETE FROM download_links; DELETE FROM file_tags; DELETE FROM files; DELETE FROM tags;";
        await command.ExecuteNonQueryAsync();
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", "/tmp/files-staging");
        Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__S3PartSizeBytes", "5242880");
        Environment.SetEnvironmentVariable("Storage__S3SinglePutThresholdBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__MaxConcurrentSessions", "3");
        Environment.SetEnvironmentVariable("Storage__StagingReserveBytes", "1048576");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", Pepper);
        Environment.SetEnvironmentVariable("S3__Region", LocalStackS3Fixture.Region);
        Environment.SetEnvironmentVariable("S3__Bucket", LocalStackS3Fixture.Bucket);
        Environment.SetEnvironmentVariable("S3__ServiceUrl", localstack.ServiceUrl);
        Environment.SetEnvironmentVariable("S3__ForcePathStyle", "true");
        Environment.SetEnvironmentVariable("S3__UseSsl", "false");
        Environment.SetEnvironmentVariable("S3__AccessKeyId", "test");
        Environment.SetEnvironmentVariable("S3__SecretAccessKey", "test");
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
}
