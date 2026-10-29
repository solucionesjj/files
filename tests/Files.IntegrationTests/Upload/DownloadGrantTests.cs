using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

namespace Files.IntegrationTests.Upload;

/// <summary>
/// S7 owner download grants against the real API host + PostgreSQL 18 + LocalStack S3: issue →
/// stream, single-use, 120 s expiry, completion-only counting, NoSuchKey → file_unavailable, and
/// no presigned URLs (the browser streams only through the API capability URL).
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class DownloadGrantTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Fact]
    public async Task Owner_download_streams_content_counts_once_and_is_single_use()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        var file = await SeedAvailableFileAsync(ownerId, "report.bin", content);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);
        using var anonymous = factory.CreateClient();

        var (token, url) = await IssueGrantAsync(client, file.Id);

        var download = await anonymous.GetAsync(url);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.Should().Contain("report.bin");
        download.Headers.CacheControl!.NoStore.Should().BeTrue();
        download.Content.Headers.TryGetValues("Content-Length", out _).Should().BeTrue();

        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);

        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString)))
        {
            var counted = await db.Files.SingleAsync(f => f.Id == file.Id);
            counted.SuccessfulDownloads.Should().Be(1);
            counted.LastDownloadAt.Should().NotBeNull();
        }

        // A single `completed` download event is recorded for the grant.
        var completedEvents = await CountDownloadEventsAsync(file.Id, "completed");
        completedEvents.Should().Be(1);

        // The same grant cannot be used again.
        var reuse = await anonymous.GetAsync(url);
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Read(reuse)).GetProperty("code").GetString().Should().Be("grant_consumed");

        await using (var db2 = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString)))
        {
            (await db2.Files.SingleAsync(f => f.Id == file.Id)).SuccessfulDownloads.Should().Be(1);
        }
    }

    [Fact]
    public async Task Expired_grant_returns_410_grant_expired()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var content = new byte[16];
        var file = await SeedAvailableFileAsync(ownerId, "expired.bin", content);

        var token = GrantTokens.Generate();
        await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString)))
        {
            db.DownloadGrants.Add(new DownloadGrant
            {
                FileId = file.Id,
                GrantTokenHash = GrantTokens.Hash(token),
                Source = GrantSource.Owner,
                IssuedToUserId = ownerId,
                State = GrantState.Issued,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/v1/download-grants/{token}");
        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await Read(response)).GetProperty("code").GetString().Should().Be("grant_expired");
    }

    [Fact]
    public async Task Unknown_token_returns_404_grant_not_found()
    {
        await ResetAsync();
        await SeedOwnerAsync();

        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/v1/download-grants/{GrantTokens.Generate()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(response)).GetProperty("code").GetString().Should().Be("grant_not_found");
    }

    [Fact]
    public async Task Missing_object_maps_to_404_file_unavailable_and_fails_the_grant()
    {
        await ResetAsync();
        var (ownerId, objectId) = await SeedOwnerAsync();
        var file = await SeedAvailableFileAsync(ownerId, "missing.bin", new byte[32], putObject: false);

        await using var factory = CreateFactory();
        using var client = ClientFor(factory, objectId);
        using var anonymous = factory.CreateClient();

        var (_, url) = await IssueGrantAsync(client, file.Id);
        var download = await anonymous.GetAsync(url);
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(download)).GetProperty("code").GetString().Should().Be("file_unavailable");

        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        (await db.Files.SingleAsync(f => f.Id == file.Id)).SuccessfulDownloads.Should().Be(0);
    }

    // ------------------------------------------------------------------ helpers

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid objectId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AuthTestJwt.CreateUserToken(objectId));
        return client;
    }

    private static async Task<(string Token, string Url)> IssueGrantAsync(HttpClient client, Guid fileId)
    {
        var response = await client.PostAsync($"/v1/portal/files/{fileId}/downloads", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await Read(response);
        return (body.GetProperty("grantToken").GetString()!, body.GetProperty("downloadUrl").GetString()!);
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

    private async Task<FileEntity> SeedAvailableFileAsync(
        Guid ownerId,
        string name,
        byte[] content,
        bool putObject = true)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerId, name, LocalStackS3Fixture.Bucket, string.Empty, content.Length, "application/octet-stream",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
        file.StorageKey = $"f/{file.Id}";
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        db.Files.Add(file);
        await db.SaveChangesAsync();

        if (putObject)
        {
            await PutObjectAsync(file.StorageKey, content);
        }

        return file;
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
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM download_events; DELETE FROM download_grants; DELETE FROM download_links; DELETE FROM file_tags; DELETE FROM files; DELETE FROM tags;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountDownloadEventsAsync(Guid fileId, string result)
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM download_events WHERE file_id = @fileId AND result = @result";
        command.Parameters.AddWithValue("fileId", fileId);
        command.Parameters.AddWithValue("result", result);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", "/tmp/files-staging");
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "integration-test-pepper");
        Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__S3PartSizeBytes", "5242880");
        Environment.SetEnvironmentVariable("Storage__S3SinglePutThresholdBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__MaxConcurrentSessions", "3");
        Environment.SetEnvironmentVariable("Storage__StagingReserveBytes", "1048576");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
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
