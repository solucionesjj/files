using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Files.Application.Deletion;
using Files.Application.Storage;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using Files.IntegrationTests.Upload;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Files.IntegrationTests.Deletion;

/// <summary>
/// S11/M6 integration coverage (real PostgreSQL 18 + LocalStack S3): physical deletion success /
/// NoSuchKey idempotency / retryable backoff / permanent failure / legal-hold skip, owner-admin-
/// integration delete flows, tombstone purge, and reconciliation (missing-object quarantine +
/// orphan move). Uses the real API host (WebApplicationFactory) plus direct worker-service
/// invocation, following the S10 retention test fixture/reset pattern.
/// </summary>
[Collection(DeletionCollectionDefinition.Name)]
public class DeletionReconciliationTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Fact]
    public async Task DeleteObject_success_marks_deleted_and_keeps_tombstone()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var fileId = await SeedPendingDeletionAsync(DeletionRequesterType.System, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            await PutObjectAsync($"f/{fileId:D}", [1, 2, 3]);

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("deleted");
            (await ScalarAsync<DateTime?>("SELECT deleted_at FROM files WHERE id = @id", fileId)).Should().NotBeNull();
            (await ScalarAsync<string?>("SELECT last_deletion_error FROM files WHERE id = @id", fileId)).Should().BeNull();
            (await ScalarAsync<DateTime?>("SELECT next_deletion_at FROM files WHERE id = @id", fileId)).Should().BeNull();
            (await ScalarAsync<string?>("SELECT storage_key FROM files WHERE id = @id", fileId)).Should().NotBeNull(); // tombstone retains key
            (await CountWhereAsync("file_audit_events", "action", "deleted")).Should().Be(1);
            (await CountWhereAsync("outbox_messages", "event_type", "file.deleted")).Should().Be(1);

            (await HeadAsync($"f/{fileId:D}")).Should().BeFalse(); // object physically gone
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task DeleteObject_is_idempotent_when_object_is_already_missing()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var fileId = await SeedPendingDeletionAsync(DeletionRequesterType.User, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            // No S3 object is created: the delete must still succeed (FR-DEL-003).

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("deleted");
            (await ScalarAsync<string?>("SELECT last_deletion_error FROM files WHERE id = @id", fileId)).Should().BeNull();
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Retryable_failure_records_backoff_and_attempts()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            var store = new FailingDeleteObjectStore(new StorageException(StorageErrorKind.Retryable, "server_error", "retry"));
            await using var factory = CreateFactory(staging).WithObjectStore(store);

            var fileId = await SeedPendingDeletionAsync(DeletionRequesterType.Integration, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("pending_deletion");
            (await ScalarAsync<int>("SELECT deletion_attempts FROM files WHERE id = @id", fileId)).Should().Be(1);
            (await ScalarAsync<string?>("SELECT last_deletion_error FROM files WHERE id = @id", fileId)).Should().Be("server_error");
            var next = await ScalarAsync<DateTime?>("SELECT next_deletion_at FROM files WHERE id = @id", fileId);
            next.Should().NotBeNull();
            next!.Value.Should().BeAfter(DateTime.UtcNow);
            (await CountWhereAsync("file_audit_events", "action", "deletion_failed")).Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Permanent_failure_alerts_and_leaves_file_non_downloadable()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            var store = new FailingDeleteObjectStore(new StorageException(StorageErrorKind.Permanent, "access_denied", "denied"));
            await using var factory = CreateFactory(staging).WithObjectStore(store);

            var fileId = await SeedPendingDeletionAsync(DeletionRequesterType.Admin, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("pending_deletion");
            (await ScalarAsync<string?>("SELECT last_deletion_error FROM files WHERE id = @id", fileId)).Should().Be("access_denied");
            (await ScalarAsync<int>("SELECT deletion_attempts FROM files WHERE id = @id", fileId)).Should().Be(0);
            (await ScalarAsync<DateTime?>("SELECT next_deletion_at FROM files WHERE id = @id", fileId)).Should().BeNull(); // no backoff
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Legal_hold_blocks_physical_deletion()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var fileId = await SeedPendingDeletionAsync(DeletionRequesterType.System, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1), legalHold: true);
            await PutObjectAsync($"f/{fileId:D}", [9, 9, 9]);

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("pending_deletion");
            (await HeadAsync($"f/{fileId:D}")).Should().BeTrue(); // object still present
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Owner_admin_and_integration_deletes_flow_through_physical_deletion()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var owner = await SeedPendingDeletionAsync(DeletionRequesterType.User, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            var admin = await SeedPendingDeletionAsync(DeletionRequesterType.Admin, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            var integration = await SeedPendingDeletionAsync(DeletionRequesterType.Integration, nextDeletionAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            await PutObjectAsync($"f/{owner:D}", [1]);
            await PutObjectAsync($"f/{admin:D}", [2]);
            await PutObjectAsync($"f/{integration:D}", [3]);

            await RunAsync<IDeletionService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(owner)).Should().Be("deleted");
            (await StatusOfAsync(admin)).Should().Be("deleted");
            (await StatusOfAsync(integration)).Should().Be("deleted");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Tombstone_purge_nulls_storage_key_and_retains_metadata()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var fileId = await SeedDeletedTombstoneAsync(deletedAt: DateTimeOffset.UtcNow.AddMonths(-61));

            await RunAsync<ITombstonePurgeService>(factory, s => s.RunOnceAsync());

            (await ScalarAsync<string?>("SELECT storage_key FROM files WHERE id = @id", fileId)).Should().BeNull();
            (await StatusOfAsync(fileId)).Should().Be("deleted"); // metadata retained
            (await CountWhereAsync("file_audit_events", "action", "tombstone_purged")).Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Reconcile_quarantines_available_files_with_a_missing_object()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var fileId = await SeedAvailableAsync();
            // No S3 object is created.

            await RunAsync<IReconciliationService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("quarantined");
            (await ScalarAsync<string?>("SELECT quarantine_reason FROM files WHERE id = @id", fileId)).Should().Be("object_missing");
            (await CountWhereAsync("file_audit_events", "action", "quarantined")).Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Reconcile_moves_f_orphans_and_deletes_staging_orphans()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var orphanKey = $"f/{Guid.NewGuid():N}";
            await PutObjectAsync(orphanKey, [4, 5, 6]);
            await PutObjectAsync("staging/part-orphan", [7, 8]);

            await RunAsync<IReconciliationService>(factory, s => s.RunOnceAsync());

            (await HeadAsync(orphanKey)).Should().BeFalse(); // original gone
            (await HeadAsync($"orphan/{orphanKey}")).Should().BeTrue(); // moved to orphan/
            (await HeadAsync("staging/part-orphan")).Should().BeFalse(); // staging deleted
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ seeding

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var user = Files.Domain.Entities.User.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Owner", "owner@test.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedPendingDeletionAsync(
        DeletionRequesterType requester,
        DateTimeOffset nextDeletionAt,
        bool legalHold = false)
    {
        var ownerUserId = await SeedUserAsync();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerUserId, "doomed.pdf", LocalStackS3Fixture.Bucket, $"f/{Guid.NewGuid():N}", 1234, "application/pdf",
            Files.Domain.ValueObjects.Retention.Finite(DateTimeOffset.UtcNow.AddDays(-1), 30));
        file.StorageKey = $"f/{file.Id:D}";
        file.Status = FileStatus.PendingDeletion;
        file.ScanStatus = ScanStatus.Clean;
        file.AvailableAt = DateTimeOffset.UtcNow.AddDays(-2);
        file.DeletionRequestedByType = requester;
        file.NextDeletionAt = nextDeletionAt;
        file.LegalHold = legalHold;
        file.LegalHoldSetAt = legalHold ? DateTimeOffset.UtcNow : null;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> SeedAvailableAsync()
    {
        var ownerUserId = await SeedUserAsync();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerUserId, "live.pdf", LocalStackS3Fixture.Bucket, $"f/{Guid.NewGuid():N}", 55, "application/pdf",
            Files.Domain.ValueObjects.Retention.Finite(DateTimeOffset.UtcNow.AddDays(10), 30));
        file.StorageKey = $"f/{file.Id:D}";
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        file.AvailableAt = DateTimeOffset.UtcNow;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> SeedDeletedTombstoneAsync(DateTimeOffset deletedAt)
    {
        var ownerUserId = await SeedUserAsync();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerUserId, "old.pdf", LocalStackS3Fixture.Bucket, $"f/{Guid.NewGuid():N}", 42, "application/pdf",
            Files.Domain.ValueObjects.Retention.Finite(DateTimeOffset.UtcNow.AddYears(-6), 30));
        file.StorageKey = $"f/{file.Id:D}";
        file.Status = FileStatus.Deleted;
        file.ScanStatus = ScanStatus.Clean;
        file.DeletedAt = deletedAt;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    // ------------------------------------------------------------------ S3 helpers

    private AmazonS3Client S3Client()
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            UseHttp = true,
            AuthenticationRegion = LocalStackS3Fixture.Region,
            ServiceURL = localstack.ServiceUrl,
        };

        return new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);
    }

    private async Task PutObjectAsync(string key, byte[] content)
    {
        using var client = S3Client();
        using var stream = new MemoryStream(content);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = LocalStackS3Fixture.Bucket,
            Key = key,
            InputStream = stream,
        });
    }

    private async Task<bool> HeadAsync(string key)
    {
        using var client = S3Client();
        try
        {
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = LocalStackS3Fixture.Bucket,
                Key = key,
            });
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task ClearBucketAsync()
    {
        using var client = S3Client();
        var response = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = LocalStackS3Fixture.Bucket });
        foreach (var obj in response.S3Objects ?? [])
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = LocalStackS3Fixture.Bucket,
                Key = obj.Key,
            });
        }
    }

    // ------------------------------------------------------------------ DB helpers

    private async Task ResetStateAsync()
    {
        await ClearBucketAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM notification_delivery_attempts;
            DELETE FROM notification_events;
            DELETE FROM retention_migrations;
            DELETE FROM admin_audit_events;
            DELETE FROM file_audit_events;
            DELETE FROM download_events;
            DELETE FROM download_grants;
            DELETE FROM download_links;
            DELETE FROM integration_file_grants;
            DELETE FROM upload_chunks;
            DELETE FROM upload_sessions;
            DELETE FROM files;
            DELETE FROM file_tags;
            DELETE FROM tags;
            DELETE FROM integration_api_keys;
            DELETE FROM integrations;
            DELETE FROM outbox_messages;
            DELETE FROM idempotency_records;
            DELETE FROM daily_usage;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> StatusOfAsync(Guid fileId) =>
        (await ScalarAsync<string>("SELECT status FROM files WHERE id = @id", fileId))!;

    private async Task<long> CountWhereAsync(string table, string column, object value)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table} WHERE {column} = @value";
        cmd.Parameters.AddWithValue("value", value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<T?> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private static async Task RunAsync<TInterface>(WebApplicationFactory<Program> factory, Func<TInterface, Task> run)
        where TInterface : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TInterface>();
        await run(service);
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "deletion-test-pepper");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
        Environment.SetEnvironmentVariable("S3__Region", LocalStackS3Fixture.Region);
        Environment.SetEnvironmentVariable("S3__Bucket", LocalStackS3Fixture.Bucket);
        Environment.SetEnvironmentVariable("S3__ServiceUrl", localstack.ServiceUrl);
        Environment.SetEnvironmentVariable("S3__ForcePathStyle", "true");
        Environment.SetEnvironmentVariable("S3__UseSsl", "false");
        Environment.SetEnvironmentVariable("S3__AccessKeyId", "test");
        Environment.SetEnvironmentVariable("S3__SecretAccessKey", "test");

        return new WebApplicationFactory<Program>();
    }

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-deletion-" + Guid.NewGuid().ToString("N"));

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

    // ------------------------------------------------------------------ fake store

    private sealed class FailingDeleteObjectStore(StorageException failure) : IObjectStore
    {
        public Task DeleteObjectAsync(string key, CancellationToken ct = default) => throw failure;

        public Task<string> PutObjectAsync(string key, Stream content, long sizeBytes, string contentType, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<string> InitiateMultipartAsync(string key, string contentType, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<UploadedPart> UploadPartAsync(string key, string uploadId, int partNumber, Stream content, long sizeBytes, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<string> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ObjectHead> HeadObjectAsync(string key, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Stream> GetObjectAsync(string key, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Stream> GetObjectAsync(string key, long offset, long length, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ObjectListEntry>> ListObjectsAsync(string prefix, int maxKeys, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task CopyObjectAsync(string sourceKey, string destinationKey, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}

internal static class DeletionTestFactoryExtensions
{
    public static WebApplicationFactory<Program> WithObjectStore(
        this WebApplicationFactory<Program> factory,
        IObjectStore store) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IObjectStore>();
            services.AddSingleton(store);
        }));
}
