using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Auth;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// Upload tampering and denial coverage (T-01, T-02, T-05, FR-UP): filename path traversal and
/// CRLF/header injection, content-type injection, negative/zero/oversized size edges, oversized
/// chunks, and whole-file checksum mismatch. The malware gate (EICAR → quarantine, scanner-down →
/// stays unavailable) is covered by <see cref="ScanCommitTests"/>.
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class UploadTamperingTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("a/b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("a\r\nb.txt")]
    [InlineData("a\nb.txt")]
    [InlineData("a\tb.txt")]
    [InlineData("  ")]
    [InlineData("")]
    public async Task Create_rejects_path_traversal_and_control_characters(string fileName)
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory);

            var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName,
                sizeBytes = 10,
                contentType = "text/plain",
            });

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(response)).GetProperty("code").GetString().Should().Be("invalid_filename");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Theory]
    [InlineData("text/plain\r\nX-Evil: 1")]
    [InlineData("text/plain\nInjected: yes")]
    [InlineData("<>")]
    [InlineData("")]
    public async Task Create_rejects_content_type_injection(string contentType)
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory);

            var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "a.txt",
                sizeBytes = 10,
                contentType,
            });

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(response)).GetProperty("code").GetString().Should().Be("unsupported_content_type");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Create_rejects_negative_size()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory);

            var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "a.txt",
                sizeBytes = -1,
                contentType = "text/plain",
            });

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(response)).GetProperty("code").GetString().Should().Be("validation_error");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Create_accepts_zero_size_as_a_valid_empty_file()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory);

            var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "empty.txt",
                sizeBytes = 0,
                contentType = "text/plain",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Create_rejects_file_exceeding_the_max_chunk_count()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            // 100-byte chunks, max 2 chunks ⇒ a 1000-byte file needs 10 chunks and must be rejected.
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Storage__ChunkSizeBytes"] = "100",
                ["Storage__MaxChunkCount"] = "2",
            });
            using var client = PortalClient(factory);

            var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
            {
                fileName = "big.txt",
                sizeBytes = 1000,
                contentType = "text/plain",
            });

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(response)).GetProperty("code").GetString().Should().Be("file_too_large");
        }
        finally
        {
            // These env overrides are process-wide; restore the shared Upload-collection baseline.
            Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
            Environment.SetEnvironmentVariable("Storage__MaxChunkCount", null);
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Integration_create_rejects_file_over_the_per_integration_maximum()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            // The DB override wins over Storage__MaxFileSizeBytes (V2 seeds 5 GiB), so set the
            // platform setting directly and restore it afterwards (shared collection fixture).
            await SetPlatformSettingAsync("max_file_size_bytes", "1024");
            try
            {
                await using var factory = CreateFactory(staging);
                using var client = factory.CreateClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", await SeedIntegrationKeyAsync(["file.write"]));

                var response = await client.PostAsJsonAsync("/v1/integrations/upload-sessions", new
                {
                    fileName = "big.txt",
                    sizeBytes = 2048,
                    contentType = "text/plain",
                });

                response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
                (await Read(response)).GetProperty("code").GetString().Should().Be("file_too_large");
            }
            finally
            {
                await SetPlatformSettingAsync("max_file_size_bytes", "5368709120");
            }
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Chunk_larger_than_the_chunk_size_is_rejected()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Storage__ChunkSizeBytes"] = "100",
            });
            using var client = PortalClient(factory);

            var session = await CreateSessionAsync(client, 100);
            var response = await PutChunkAsync(client, session, 1, new byte[200]);

            response.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Complete_with_a_mismatched_whole_file_checksum_is_rejected()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetAsync();
            await using var factory = CreateFactory(staging);
            using var client = PortalClient(factory);

            var content = new byte[100];
            var session = await CreateSessionAsync(client, content.Length);

            (await PutChunkAsync(client, session, 1, content)).Should().Be(HttpStatusCode.OK);

            var wrong = new string('0', 64);
            var complete = await client.PostAsJsonAsync($"/v1/portal/upload-sessions/{session}/complete", new { checksumSha256 = wrong });

            complete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await Read(complete)).GetProperty("code").GetString().Should().Be("upload_checksum_mismatch");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static HttpClient PortalClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthTestJwt.CreateUserToken(Guid.NewGuid()));
        return client;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, int sizeBytes)
    {
        var response = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
        {
            fileName = "tamper.txt",
            sizeBytes,
            contentType = "text/plain",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Read(response)).GetProperty("uploadSessionId").GetGuid();
    }

    private static async Task<HttpStatusCode> PutChunkAsync(HttpClient client, Guid session, int number, byte[] content)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/v1/portal/upload-sessions/{session}/chunks/{number}")
        {
            Content = body,
        };
        var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<string> SeedIntegrationKeyAsync(string[] scopes)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var integration = Integration.Create("tamper-int-" + Guid.NewGuid().ToString("N")[..6], "Tamper Int");
        foreach (var permission in scopes)
        {
            integration.Permissions.Add(new IntegrationPermissionEntry { IntegrationId = integration.Id, Permission = permission });
        }

        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var prefix = ApiKeys.GeneratePrefix();
        var secret = ApiKeys.GenerateSecret();
        var fullKey = ApiKeys.Format(prefix, secret);
        db.IntegrationApiKeys.Add(new IntegrationApiKey
        {
            IntegrationId = integration.Id,
            Name = "tamper",
            Prefix = prefix,
            SecretHash = ApiKeys.Hash(fullKey),
            Scopes = scopes,
        });
        await db.SaveChangesAsync();
        return fullKey;
    }

    private async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM upload_chunks; DELETE FROM upload_sessions; DELETE FROM files; DELETE FROM integration_api_keys; DELETE FROM integration_permissions; DELETE FROM integrations; DELETE FROM users;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetPlatformSettingAsync(string key, string value)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO platform_settings (key, value) VALUES (@key, @value::jsonb) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()";
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", value);
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

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-tamper-" + Guid.NewGuid().ToString("N"));

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
