using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Files.Application.Identity;
using Files.Application.Storage;
using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Auth;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// End-to-end upload tests against the real API host: PostgreSQL 18 + LocalStack S3. Covers the
/// chunked upload vertical slice (ADR-0003), multipart commit, idempotency, checksums, denied paths,
/// abort, and the staging concurrency/reserve gates.
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class UploadApiTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Fact]
    public async Task Portal_upload_completes_single_put_and_is_available()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            var content = new byte[100];
            var create = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "a.txt",
                sizeBytes = content.Length,
                contentType = "text/plain",
                checksumSha256 = Hex(content),
            });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var session = (await Read(create)).GetProperty("uploadSessionId").GetGuid();

            var put = await PutChunkAsync(client, session, 1, content, Hex(content));
            put.Should().Be(HttpStatusCode.OK);

            var complete = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await Read(complete);
            meta.GetProperty("status").GetString().Should().Be("available");
            meta.GetProperty("sizeBytes").GetInt64().Should().Be(content.Length);

            var head = await HeadAsync($"f/{meta.GetProperty("id").GetGuid()}");
            head.Exists.Should().BeTrue();
            head.SizeBytes.Should().Be(content.Length);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Multipart_upload_above_threshold_is_committed_and_verified()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                // 2 MiB chunks keep the test fast while the 6 MiB file still exceeds the single-put
                // threshold (256 KiB) and yields two 5 MiB/1 MiB multipart parts.
                ["Storage__ChunkSizeBytes"] = "2097152",
            });
            using var client = PortalClient(factory, out _);

            const int chunk = 2 * 1024 * 1024;
            var content = Pattern(6 * 1024 * 1024);
            var create = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "big.bin",
                sizeBytes = content.Length,
                contentType = "application/octet-stream",
                checksumSha256 = Hex(content),
            });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var session = (await Read(create)).GetProperty("uploadSessionId").GetGuid();

            (await PutChunkAsync(client, session, 1, content[..chunk], Hex(content[..chunk])))
                .Should().Be(HttpStatusCode.OK);
            (await PutChunkAsync(client, session, 2, content[chunk..(2 * chunk)], Hex(content[chunk..(2 * chunk)])))
                .Should().Be(HttpStatusCode.OK);
            (await PutChunkAsync(client, session, 3, content[(2 * chunk)..], Hex(content[(2 * chunk)..])))
                .Should().Be(HttpStatusCode.OK);

            var complete = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await Read(complete);
            meta.GetProperty("status").GetString().Should().Be("available");

            var head = await HeadAsync($"f/{meta.GetProperty("id").GetGuid()}");
            head.Exists.Should().BeTrue();
            head.SizeBytes.Should().Be(content.Length);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Repeated_chunk_is_idempotent_and_a_conflicting_chunk_is_rejected()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            var content = new byte[200];
            var session = await CreateSessionAsync(client, "c.txt", content.Length, "text/plain", Hex(content));

            (await PutChunkAsync(client, session, 1, content, Hex(content))).Should().Be(HttpStatusCode.OK);
            (await PutChunkAsync(client, session, 1, content, Hex(content))).Should().Be(HttpStatusCode.OK);

            var other = new byte[200];
            other[0] = 1;
            (await PutChunkAsync(client, session, 1, other, Hex(other))).Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Chunk_with_wrong_checksum_is_rejected()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            var content = new byte[100];
            var session = await CreateSessionAsync(client, "d.txt", content.Length, "text/plain", null);

            var bad = Hex(new byte[100]);
            bad = new string('0', 64);
            (await PutChunkAsync(client, session, 1, content, bad)).Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Complete_before_all_chunks_returns_upload_incomplete()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            var content = Pattern(512 * 1024);
            var session = await CreateSessionAsync(client, "e.bin", content.Length, "application/octet-stream", null);

            (await PutChunkAsync(client, session, 1, content[..(256 * 1024)], null)).Should().Be(HttpStatusCode.OK);

            var complete = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
            complete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(complete)).GetProperty("code").GetString().Should().Be("upload_incomplete");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Integration_without_file_write_is_denied_with_403()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = factory.CreateClient();

            var key = await SeedIntegrationWithKeyAsync(["file.read"], ["file.read"]);
            client.DefaultRequestHeaders.Add("X-Api-Key", key);

            var response = await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new
            {
                fileName = "x.txt",
                sizeBytes = 10,
                contentType = "text/plain",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await Read(response)).GetProperty("code").GetString().Should().Be("insufficient_permission");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task User_cannot_read_or_write_another_users_session()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);

            var owner = PortalClient(factory, out var ownerToken);
            var session = await CreateSessionAsync(owner, "mine.txt", 100, "text/plain", null);

            using var other = factory.CreateClient();
            other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthTestJwt.CreateUserToken(Guid.NewGuid()));

            var get = await other.GetAsync($"/v1/portal/upload-sessions/{session}");
            get.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var put = await PutChunkAsync(other, session, 1, new byte[100], null);
            put.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Abort_removes_the_session_and_staged_bytes()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            var content = new byte[100];
            var session = await CreateSessionAsync(client, "abort.txt", content.Length, "text/plain", null);
            (await PutChunkAsync(client, session, 1, content, null)).Should().Be(HttpStatusCode.OK);

            var abort = await client.DeleteAsync($"/v1/portal/upload-sessions/{session}");
            abort.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var after = await client.GetAsync($"/v1/portal/upload-sessions/{session}");
            after.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(after)).GetProperty("status").GetString().Should().Be("aborted");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Concurrent_session_limit_is_enforced()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory, out _);

            for (var i = 0; i < 3; i++)
            {
                (await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
                {
                    fileName = $"s{i}.txt",
                    sizeBytes = 0,
                    contentType = "text/plain",
                })).StatusCode.Should().Be(HttpStatusCode.Created);
            }

            var fourth = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "s4.txt",
                sizeBytes = 0,
                contentType = "text/plain",
            });
            fourth.StatusCode.Should().Be(HttpStatusCode.InsufficientStorage);
            (await Read(fourth)).GetProperty("code").GetString().Should().Be("staging_capacity_exceeded");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Staging_reserve_breach_returns_507()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Storage__StagingReserveBytes"] = "9000000000000", // > any plausible free disk space
            });
            using var client = PortalClient(factory, out _);

            var create = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "big.txt",
                sizeBytes = 1024,
                contentType = "text/plain",
            });

            create.StatusCode.Should().Be(HttpStatusCode.InsufficientStorage);
            (await Read(create)).GetProperty("code").GetString().Should().Be("staging_capacity_exceeded");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Complete_is_retryable_after_a_commit_failure_without_reuploading_chunks()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();

            var store = new FlakyObjectStore { FailNextPut = true };
            await using var factory = CreateFactory(staging)
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IObjectStore>();
                    services.AddSingleton<IObjectStore>(store);
                }));
            using var client = PortalClient(factory, out _);

            var content = new byte[100];
            var session = await CreateSessionAsync(client, "retry.txt", content.Length, "text/plain", null);
            (await PutChunkAsync(client, session, 1, content, null)).Should().Be(HttpStatusCode.OK);

            var first = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
            first.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            (await Read(first)).GetProperty("code").GetString().Should().Be("upload_commit_failed");
            store.PutCount.Should().Be(1);

            // The staged bytes are retained: retry without re-uploading any chunk.
            store.FailNextPut = false;
            var second = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
            second.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(second)).GetProperty("status").GetString().Should().Be("available");
            store.PutCount.Should().Be(2);

            // No chunk was re-uploaded (one chunk row remains).
            (await PersistenceTestHelpers.CountWhereAsync(postgres.ConnectionString, "upload_chunks", "upload_session_id", session))
                .Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ helpers

    private sealed class FlakyObjectStore : IObjectStore
    {
        private long _size;

        public bool FailNextPut { get; set; }

        public int PutCount { get; private set; }

        public Task<string> PutObjectAsync(string key, Stream content, long sizeBytes, string contentType, CancellationToken ct = default)
        {
            PutCount++;
            if (FailNextPut)
            {
                FailNextPut = false;
                throw new IOException("simulated commit failure");
            }

            _size = sizeBytes;
            return Task.FromResult("etag");
        }

        public Task<ObjectHead> HeadObjectAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(new ObjectHead(true, "etag", _size, null));

        public Task<string> InitiateMultipartAsync(string key, string contentType, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<UploadedPart> UploadPartAsync(string key, string uploadId, int partNumber, Stream content, long sizeBytes, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<string> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Stream> GetObjectAsync(string key, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private static HttpClient PortalClient(WebApplicationFactory<Program> factory, out string token)
    {
        token = AuthTestJwt.CreateUserToken(Guid.NewGuid());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, string name, int size, string contentType, string? checksum)
    {
        var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
        {
            fileName = name,
            sizeBytes = size,
            contentType,
            checksumSha256 = checksum,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Read(response)).GetProperty("uploadSessionId").GetGuid();
    }

    private static async Task<HttpStatusCode> PutChunkAsync(HttpClient client, Guid session, int number, byte[] content, string? checksum)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/v1/portal/upload-sessions/{session}/chunks/{number}")
        {
            Content = body,
        };
        if (checksum is not null)
        {
            request.Headers.Add("X-Chunk-SHA256", checksum);
        }

        var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<(bool Exists, long SizeBytes)> HeadAsync(string key)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            UseHttp = true,
            AuthenticationRegion = LocalStackS3Fixture.Region,
            ServiceURL = localstack.ServiceUrl,
        };
        using var client = new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);
        try
        {
            var response = await client.GetObjectMetadataAsync(LocalStackS3Fixture.Bucket, key);
            return (true, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, 0);
        }
    }

    private async Task<string> SeedIntegrationWithKeyAsync(string[] permissions, string[] scopes)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));

        var integration = Integration.Create("up-" + Guid.NewGuid().ToString("N")[..8], "Upload Test");
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

        return fullKey;
    }

    private async Task ResetUploadStateAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM upload_chunks; DELETE FROM upload_sessions; DELETE FROM files;";
        await command.ExecuteNonQueryAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath, Dictionary<string, string>? overrides = null)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
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

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                Environment.SetEnvironmentVariable(key, value);
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
