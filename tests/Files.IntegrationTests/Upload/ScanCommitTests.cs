using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Files.Application.Identity;
using Files.Application.Storage;
using Files.Application.Upload;
using Files.Domain.Enums;
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
/// Exercises the scan/commit worker vertical slice (ADR-0003, D-35): clean files are scanned by a
/// real ClamAV daemon and committed to S3; infected files are quarantined and never uploaded; and a
/// scanner failure leaves the file <c>processing</c>/<c>scan_status=failed</c> for a retry.
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class ScanCommitTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack, ClamAvFixture clamav)
{
    [Trait("Category", "ClamAV")]
    [Fact]
    public async Task Clean_file_is_scanned_and_committed_to_available()
    {
        if (!clamav.Available)
        {
            return; // ClamAV unavailable (Apple Silicon emulation / FILES_CLAMAV_TESTS=0); runs in CI.
        }

        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Features__MalwareScanning"] = "true",
                ["ClamAV__Host"] = "127.0.0.1",
                ["ClamAV__Port"] = clamav.Port.ToString(CultureInfo.InvariantCulture),
            });
            using var client = PortalClient(factory);

            var content = Encoding.ASCII.GetBytes("a clean file for ClamAV");
            var fileId = await CreateAndCompleteAsync(client, "clean.txt", content, "text/plain", Hex(content));

            // Complete must not scan or commit in the request: the file is handed to the worker.
            var afterComplete = await ReadFileAsync(fileId);
            afterComplete.Status.Should().Be(FileStatus.Processing);
            afterComplete.ScanStatus.Should().Be(ScanStatus.Pending);

            await RunScanCommitAsync(factory);

            var file = await ReadFileAsync(fileId);
            file.Status.Should().Be(FileStatus.Available);
            file.ScanStatus.Should().Be(ScanStatus.Clean);

            var head = await HeadAsync($"f/{fileId:D}");
            head.Exists.Should().BeTrue();
            head.SizeBytes.Should().Be(content.Length);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Trait("Category", "ClamAV")]
    [Fact]
    public async Task Eicar_file_is_quarantined_and_never_uploaded()
    {
        if (!clamav.Available)
        {
            return; // ClamAV unavailable (Apple Silicon emulation / FILES_CLAMAV_TESTS=0); runs in CI.
        }

        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Features__MalwareScanning"] = "true",
                ["ClamAV__Host"] = "127.0.0.1",
                ["ClamAV__Port"] = clamav.Port.ToString(CultureInfo.InvariantCulture),
            });
            using var client = PortalClient(factory);

            var eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
            var fileId = await CreateAndCompleteAsync(client, "eicar.com", eicar, "text/plain", Hex(eicar));

            await RunScanCommitAsync(factory);

            var file = await ReadFileAsync(fileId);
            file.Status.Should().Be(FileStatus.Quarantined);
            file.ScanStatus.Should().Be(ScanStatus.Infected);
            file.QuarantineReason.Should().NotBeNullOrWhiteSpace();

            // The infected object must never reach S3.
            var head = await HeadAsync($"f/{fileId:D}");
            head.Exists.Should().BeFalse();
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Scanner_failure_keeps_processing_failed_and_retries()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetUploadStateAsync();

            var scanner = new FakeMalwareScanner(ScanResult.Failed, ScanResult.Clean);
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Features__MalwareScanning"] = "true",
            }).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMalwareScanner>();
                services.AddSingleton<IMalwareScanner>(scanner);
            }));
            using var client = PortalClient(factory);

            var content = Encoding.ASCII.GetBytes("retry after scanner failure");
            var fileId = await CreateAndCompleteAsync(client, "retry.txt", content, "text/plain", Hex(content));

            await RunScanCommitAsync(factory);

            var failed = await ReadFileAsync(fileId);
            failed.Status.Should().Be(FileStatus.Processing);
            failed.ScanStatus.Should().Be(ScanStatus.Failed);
            failed.ScanAttempts.Should().Be(1);

            // Not downloadable and staging is retained (retry without re-upload).
            var head = await HeadAsync($"f/{fileId:D}");
            head.Exists.Should().BeFalse();

            // Make the file due again and retry; the scanner now reports clean.
            await MakeDueForRetryAsync(fileId);
            await RunScanCommitAsync(factory);

            var file = await ReadFileAsync(fileId);
            file.Status.Should().Be(FileStatus.Available);
            file.ScanStatus.Should().Be(ScanStatus.Clean);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ helpers

    private sealed class FakeMalwareScanner(params ScanResult[] results) : IMalwareScanner
    {
        private readonly Queue<ScanResult> _results = new(results);

        public Task<ScanResult> ScanAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(_results.Count > 0 ? _results.Dequeue() : ScanResult.Clean);
    }

    private static async Task<Guid> CreateAndCompleteAsync(HttpClient client, string name, byte[] content, string contentType, string checksum)
    {
        var create = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
        {
            fileName = name,
            sizeBytes = content.Length,
            contentType,
            checksumSha256 = checksum,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await Read(create)).GetProperty("uploadSessionId").GetGuid();
        var fileId = (await Read(create)).GetProperty("fileId").GetGuid();

        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var put = new HttpRequestMessage(HttpMethod.Put, $"/v1/portal/upload-sessions/{session}/chunks/1")
        {
            Content = body,
        };
        put.Headers.Add("X-Chunk-SHA256", checksum);
        (await client.SendAsync(put)).StatusCode.Should().Be(HttpStatusCode.OK);

        var complete = await client.PostAsync($"/v1/portal/upload-sessions/{session}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Read(complete)).GetProperty("status").GetString().Should().Be("processing");

        return fileId;
    }

    private static async Task RunScanCommitAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScanCommitService>();
        var summary = await service.RunOnceAsync();
        summary.Processed.Should().Be(1);
    }

    private async Task<(FileStatus Status, ScanStatus ScanStatus, int ScanAttempts, string? QuarantineReason)> ReadFileAsync(Guid fileId)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = await db.Files.FindAsync(fileId);
        file.Should().NotBeNull();
        return (file!.Status, file.ScanStatus, file.ScanAttempts, file.QuarantineReason);
    }

    private async Task MakeDueForRetryAsync(Guid fileId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE files SET scan_next_attempt_at = now() - interval '1 second' WHERE id = @id";
        command.Parameters.AddWithValue("id", fileId);
        await command.ExecuteNonQueryAsync();
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

    private static HttpClient PortalClient(WebApplicationFactory<Program> factory)
    {
        var token = AuthTestJwt.CreateUserToken(Guid.NewGuid());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath, Dictionary<string, string>? overrides = null)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
        Environment.SetEnvironmentVariable("Storage__ChunkSizeBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__S3PartSizeBytes", "5242880");
        Environment.SetEnvironmentVariable("Storage__S3SinglePutThresholdBytes", "262144");
        Environment.SetEnvironmentVariable("Storage__MaxConcurrentSessions", "3");
        Environment.SetEnvironmentVariable("Storage__StagingReserveBytes", "1048576");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
        Environment.SetEnvironmentVariable("ClamAV__Host", "127.0.0.1");
        Environment.SetEnvironmentVariable("ClamAV__Port", "3310");
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

    private async Task ResetUploadStateAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM upload_chunks; DELETE FROM upload_sessions; DELETE FROM files;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-scan-" + Guid.NewGuid().ToString("N"));

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
