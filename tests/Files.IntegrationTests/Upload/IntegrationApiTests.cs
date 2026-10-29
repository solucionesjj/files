using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// End-to-end integration API (S9 / FR-INT) against the real host: PostgreSQL 18 + LocalStack S3.
/// Covers list/get/availability/content/delete, ownership isolation + grants, idempotency,
/// single-range downloads, completion-only counting, and the per-integration quotas.
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class IntegrationApiTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Fact]
    public async Task Upload_list_get_availability_content_delete_flow()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write", "file.read", "file.delete"], ["file.write", "file.read", "file.delete"]);
            using var client = IntegrationClient(factory, key);

            var content = Pattern(1024);
            var fileId = await UploadAsync(client, "flow.bin", content, "application/octet-stream");

            var list = await client.GetAsync("/v1/integrations/files?limit=10");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await Read(list);
            page.GetProperty("items").GetArrayLength().Should().Be(1);
            page.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(fileId);
            page.GetProperty("hasMore").GetBoolean().Should().BeFalse();

            var statusFilter = await client.GetAsync("/v1/integrations/files?status=available");
            (await Read(statusFilter)).GetProperty("items").GetArrayLength().Should().Be(1);

            var get = await client.GetAsync($"/v1/integrations/files/{fileId}");
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(get)).GetProperty("status").GetString().Should().Be("available");

            var availability = await client.GetAsync($"/v1/integrations/files/{fileId}/availability");
            availability.StatusCode.Should().Be(HttpStatusCode.OK);
            var av = await Read(availability);
            av.GetProperty("state").GetString().Should().Be("available");
            av.GetProperty("sizeBytes").GetInt64().Should().Be(content.Length);

            var download = await client.GetAsync($"/v1/integrations/files/{fileId}/content");
            download.StatusCode.Should().Be(HttpStatusCode.OK);
            (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);

            var delete = await client.DeleteAsync($"/v1/integrations/files/{fileId}");
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Logical delete: metadata still owned but no longer downloadable.
            (await client.GetAsync($"/v1/integrations/files/{fileId}")).StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await client.GetAsync($"/v1/integrations/files/{fileId}/content");
            after.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Read(after)).GetProperty("code").GetString().Should().Be("file_unavailable");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Foreign_file_is_404_and_a_grant_enables_read_only_access()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var (ownerId, ownerKey) = await SeedIntegrationAsync(["file.write", "file.read", "file.delete"], ["file.write", "file.read", "file.delete"]);
            var (readerId, readerKey) = await SeedIntegrationAsync(["file.read", "file.delete"], ["file.read", "file.delete"]);

            using var owner = IntegrationClient(factory, ownerKey);
            var content = Pattern(512);
            var fileId = await UploadAsync(owner, "owned.txt", content, "text/plain");

            using var reader = IntegrationClient(factory, readerKey);
            var foreign = await reader.GetAsync($"/v1/integrations/files/{fileId}");
            foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Read(foreign)).GetProperty("code").GetString().Should().Be("file_not_found");

            await GrantAsync(fileId, readerId);

            (await reader.GetAsync($"/v1/integrations/files/{fileId}")).StatusCode.Should().Be(HttpStatusCode.OK);
            var contentRead = await reader.GetAsync($"/v1/integrations/files/{fileId}/content");
            contentRead.StatusCode.Should().Be(HttpStatusCode.OK);
            (await contentRead.Content.ReadAsByteArrayAsync()).Should().Equal(content);

            // A grant is read-only: deletion by the grantee is still denied with 404.
            var deleteAttempt = await reader.DeleteAsync($"/v1/integrations/files/{fileId}");
            deleteAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The owner can still delete.
            (await owner.DeleteAsync($"/v1/integrations/files/{fileId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Idempotent_create_replays_the_response_and_rejects_reuse()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write"], ["file.write"]);
            using var client = IntegrationClient(factory, key);

            var body = new { fileName = "idem.txt", sizeBytes = 100, contentType = "text/plain" };

            var first = await PostWithIdempotencyAsync(client, "/v1/integrations/upload-sessions", "create-key-1", body);
            first.StatusCode.Should().Be(HttpStatusCode.Created);
            var firstBody = await Read(first);
            var sessionId = firstBody.GetProperty("uploadSessionId").GetGuid();

            var replay = await PostWithIdempotencyAsync(client, "/v1/integrations/upload-sessions", "create-key-1", body);
            replay.StatusCode.Should().Be(HttpStatusCode.Created);
            (await Read(replay)).GetProperty("uploadSessionId").GetGuid().Should().Be(sessionId);

            var reuse = await PostWithIdempotencyAsync(client, "/v1/integrations/upload-sessions", "create-key-1", new { fileName = "idem.txt", sizeBytes = 200, contentType = "text/plain" });
            reuse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(reuse)).GetProperty("code").GetString().Should().Be("idempotency_key_reuse");

            (await PersistenceTestHelpers.CountAsync(postgres.ConnectionString, "files")).Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Idempotent_complete_and_delete_replay()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write", "file.delete"], ["file.write", "file.delete"]);
            using var client = IntegrationClient(factory, key);

            var content = Pattern(200);
            var fileId = await UploadAsync(client, "complete-idem.txt", content, "text/plain");
            var sessionId = await SessionIdOfAsync(client, fileId);

            // complete is retried with the same key + body (empty body).
            var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/integrations/upload-sessions/{sessionId}/complete");
            completeRequest.Headers.Add("Idempotency-Key", "complete-key-1");
            var complete = await client.SendAsync(completeRequest);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);

            var completeReplay = new HttpRequestMessage(HttpMethod.Post, $"/v1/integrations/upload-sessions/{sessionId}/complete");
            completeReplay.Headers.Add("Idempotency-Key", "complete-key-1");
            var replay = await client.SendAsync(completeReplay);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(replay)).GetProperty("id").GetGuid().Should().Be(fileId);

            // delete is idempotent under the same key.
            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/v1/integrations/files/{fileId}");
            deleteRequest.Headers.Add("Idempotency-Key", "delete-key-1");
            (await client.SendAsync(deleteRequest)).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var deleteReplay = new HttpRequestMessage(HttpMethod.Delete, $"/v1/integrations/files/{fileId}");
            deleteReplay.Headers.Add("Idempotency-Key", "delete-key-1");
            (await client.SendAsync(deleteReplay)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Range_returns_206_partial_and_416_when_unsatisfiable()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write", "file.read"], ["file.write", "file.read"]);
            using var client = IntegrationClient(factory, key);

            var content = Pattern(1000);
            var fileId = await UploadAsync(client, "range.bin", content, "application/octet-stream");

            using var partial = new HttpRequestMessage(HttpMethod.Get, $"/v1/integrations/files/{fileId}/content");
            partial.Headers.Range = new RangeHeaderValue(0, 99);
            var partialResponse = await client.SendAsync(partial);
            partialResponse.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            partialResponse.Content.Headers.ContentRange!.From.Should().Be(0);
            partialResponse.Content.Headers.ContentRange!.To.Should().Be(99);
            (await partialResponse.Content.ReadAsByteArrayAsync()).Should().Equal(content[..100]);

            using var unsatisfiable = new HttpRequestMessage(HttpMethod.Get, $"/v1/integrations/files/{fileId}/content");
            unsatisfiable.Headers.Range = new RangeHeaderValue(1000, 1999);
            var unsatisfiableResponse = await client.SendAsync(unsatisfiable);
            unsatisfiableResponse.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
            (await Read(unsatisfiableResponse)).GetProperty("code").GetString().Should().Be("range_not_satisfiable");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Completed_download_increments_counter_and_daily_usage_once()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var (integrationId, key) = await SeedIntegrationAsync(["file.write", "file.read"], ["file.write", "file.read"]);
            using var client = IntegrationClient(factory, key);

            var content = Pattern(600);
            var fileId = await UploadAsync(client, "count.bin", content, "application/octet-stream");

            (await SuccessfulDownloadsAsync(fileId)).Should().Be(0);

            (await client.GetAsync($"/v1/integrations/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync($"/v1/integrations/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.OK);

            (await SuccessfulDownloadsAsync(fileId)).Should().Be(2);
            (await PersistenceTestHelpers.CountWhereAsync(postgres.ConnectionString, "download_events", "source", "integration"))
                .Should().Be(2);

            (await DailyDownloadedAsync(integrationId)).Should().Be(content.Length * 2);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Concurrent_session_quota_returns_429()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await SetSettingAsync("max_upload_sessions_per_integration", "1");
            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write"], ["file.write"]);
            using var client = IntegrationClient(factory, key);

            (await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new { fileName = "a.txt", sizeBytes = 10, contentType = "text/plain" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            var second = await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new { fileName = "b.txt", sizeBytes = 10, contentType = "text/plain" });
            second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await Read(second)).GetProperty("code").GetString().Should().Be("quota_exceeded");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Daily_upload_and_download_quotas_return_429()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await SetSettingAsync("max_upload_sessions_per_integration", "5");
            await SetSettingAsync("daily_upload_quota_bytes", "100");
            await SetSettingAsync("daily_download_quota_bytes", "100");

            await using var factory = CreateFactory(staging);
            var (_, key) = await SeedIntegrationAsync(["file.write", "file.read"], ["file.write", "file.read"]);
            using var client = IntegrationClient(factory, key);

            var create = await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new { fileName = "big.txt", sizeBytes = 200, contentType = "text/plain" });
            create.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await Read(create)).GetProperty("code").GetString().Should().Be("quota_exceeded");

            // Upload a small file, then exhaust the download quota.
            await SetSettingAsync("daily_upload_quota_bytes", "1000000");
            var content = Pattern(200);
            var fileId = await UploadAsync(client, "small.bin", content, "application/octet-stream");

            await SetSettingAsync("daily_download_quota_bytes", "100");
            var download = await client.GetAsync($"/v1/integrations/files/{fileId}/content");
            download.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await Read(download)).GetProperty("code").GetString().Should().Be("quota_exceeded");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Guid IntegrationId, string Key)> SeedIntegrationAsync(string[] permissions, string[] scopes)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));

        var integration = Integration.Create("it-" + Guid.NewGuid().ToString("N")[..8], "Integration API Test");
        foreach (var permission in permissions)
        {
            integration.Permissions.Add(new IntegrationPermissionEntry { IntegrationId = integration.Id, Permission = permission });
        }

        var prefix = ApiKeys.GeneratePrefix();
        var secret = ApiKeys.GenerateSecret();
        var fullKey = ApiKeys.Format(prefix, secret);

        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        db.IntegrationApiKeys.Add(new IntegrationApiKey
        {
            IntegrationId = integration.Id,
            Name = "test",
            Prefix = prefix,
            SecretHash = ApiKeys.Hash(fullKey),
            Scopes = scopes,
        });
        await db.SaveChangesAsync();

        return (integration.Id, fullKey);
    }

    private static HttpClient IntegrationClient(WebApplicationFactory<Program> factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName, byte[] content, string contentType)
    {
        var create = await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new
        {
            fileName,
            sizeBytes = content.Length,
            contentType,
            checksumSha256 = Hex(content),
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await Read(create)).GetProperty("uploadSessionId").GetGuid();

        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var put = new HttpRequestMessage(HttpMethod.Put, $"/v1/integrations/upload-sessions/{session}/chunks/1")
        {
            Content = body,
        };
        put.Headers.Add("X-Chunk-SHA256", Hex(content));
        (await client.SendAsync(put)).StatusCode.Should().Be(HttpStatusCode.OK);

        var complete = await client.PostAsync($"/v1/integrations/upload-sessions/{session}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Read(complete)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SessionIdOfAsync(HttpClient client, Guid fileId)
    {
        // Find the upload session by listing sessions is not exposed; recover it from the DB instead.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM upload_sessions WHERE file_id = @fileId";
        command.Parameters.AddWithValue("fileId", fileId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task GrantAsync(Guid fileId, Guid integrationId)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        db.IntegrationFileGrants.Add(new IntegrationFileGrant { FileId = fileId, IntegrationId = integrationId });
        await db.SaveChangesAsync();
    }

    private async Task<long> SuccessfulDownloadsAsync(Guid fileId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT successful_downloads FROM files WHERE id = @fileId";
        command.Parameters.AddWithValue("fileId", fileId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> DailyDownloadedAsync(Guid integrationId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(bytes_downloaded,0) FROM daily_usage WHERE scope_type='integration' AND scope_id=@id";
        command.Parameters.AddWithValue("id", integrationId);
        var result = await command.ExecuteScalarAsync();
        return result is null ? 0 : (long)result;
    }

    private async Task SetSettingAsync(string key, string value)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO platform_settings (key, value) VALUES (@key, @value::jsonb) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()";
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HttpResponseMessage> PostWithIdempotencyAsync(HttpClient client, string url, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task ResetStateAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM idempotency_records;
            DELETE FROM daily_usage;
            DELETE FROM download_events;
            DELETE FROM file_audit_events;
            DELETE FROM upload_chunks;
            DELETE FROM upload_sessions;
            DELETE FROM download_grants;
            DELETE FROM download_links;
            DELETE FROM integration_file_grants;
            DELETE FROM files;
            DELETE FROM integration_api_keys;
            DELETE FROM integrations;
            DELETE FROM outbox_messages;
            """;
        await command.ExecuteNonQueryAsync();

        // Reset any quota overrides to defaults for a clean slate.
        await SetSettingAsync("max_upload_sessions_per_integration", "5");
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM platform_settings WHERE key IN ('daily_upload_quota_bytes','daily_download_quota_bytes')";
        await delete.ExecuteNonQueryAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath, Dictionary<string, string>? overrides = null)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "integration-test-pepper");
        Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__S3PartSizeBytes", "5242880");
        Environment.SetEnvironmentVariable("Storage__S3SinglePutThresholdBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__MaxConcurrentSessions", "10");
        Environment.SetEnvironmentVariable("Storage__StagingReserveBytes", "1048576");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
        Environment.SetEnvironmentVariable("S3__Region", LocalStackS3Fixture.Region);
        Environment.SetEnvironmentVariable("S3__Bucket", LocalStackS3Fixture.Bucket);
        Environment.SetEnvironmentVariable("S3__ServiceUrl", localstack.ServiceUrl);
        Environment.SetEnvironmentVariable("S3__ForcePathStyle", "true");
        Environment.SetEnvironmentVariable("S3__UseSsl", "false");
        Environment.SetEnvironmentVariable("S3__AccessKeyId", "test");
        Environment.SetEnvironmentVariable("S3__SecretAccessKey", "test");

        if (overrides is not null)
        {
            foreach (var (k, v) in overrides)
            {
                Environment.SetEnvironmentVariable(k, v);
            }
        }

        return new WebApplicationFactory<Program>();
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-staging-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
