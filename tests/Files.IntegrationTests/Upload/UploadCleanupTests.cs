using Files.Application.Upload;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.ValueObjects;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// Exercises <see cref="UploadCleanupService"/> against the real PostgreSQL 18 schema and a real
/// staging directory: expired open sessions are failed and their staging removed, and orphaned
/// <c>*.part</c> files are deleted (ADR-0003 §6).
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class UploadCleanupTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack)
{
    [Fact]
    public async Task Cleanup_expires_stale_sessions_and_removes_staging()
    {
        await ResetAsync();

        var staging = NewStagingPath();
        Directory.CreateDirectory(staging);
        try
        {
            var fileId = Guid.CreateVersion7();
            var sessionId = Guid.CreateVersion7();
            var stagingFile = Path.Combine(staging, $"{sessionId:D}.part");
            await File.WriteAllBytesAsync(stagingFile, [1, 2, 3]);

            await InsertExpiredSessionAsync(fileId, sessionId, stagingFile);

            await using var factory = CreateFactory(staging);
            await using var scope = factory.Services.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IUploadCleanupService>();

            var summary = await cleanup.RunOnceAsync();

            summary.ExpiredSessions.Should().Be(1);
            File.Exists(stagingFile).Should().BeFalse("expired sessions must have their staging removed");

            await using (var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString)))
            {
                var session = await db.UploadSessions.SingleAsync(s => s.Id == sessionId);
                session.Status.Should().Be(UploadSessionStatus.Expired);
            }
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Cleanup_removes_orphaned_staging_files()
    {
        await ResetAsync();

        var staging = NewStagingPath();
        Directory.CreateDirectory(staging);
        try
        {
            var orphan = Path.Combine(staging, $"{Guid.CreateVersion7():D}.part");
            await File.WriteAllBytesAsync(orphan, [1, 2, 3]);

            await using var factory = CreateFactory(staging);
            await using var scope = factory.Services.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IUploadCleanupService>();

            var summary = await cleanup.RunOnceAsync();

            summary.DeletedOrphans.Should().Be(1);
            File.Exists(orphan).Should().BeFalse("staging files without an active session must be removed");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task InsertExpiredSessionAsync(Guid fileId, Guid sessionId, string stagingFile)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));

        var owner = User.Create(Guid.NewGuid(), Guid.NewGuid(), "Cleanup Owner");
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var file = FileEntity.CreateHuman(
            owner.Id, "old.txt", "files-local", $"f/{fileId:D}", 3, "text/plain",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
        file.Id = fileId;

        db.Files.Add(file);
        db.UploadSessions.Add(new UploadSession
        {
            Id = sessionId,
            FileId = fileId,
            InitiatorType = UploadInitiatorType.User,
            InitiatorId = owner.Id,
            Status = UploadSessionStatus.Initiated,
            ExpectedSizeBytes = 3,
            ExpectedContentType = "text/plain",
            ChunkSizeBytes = 262144,
            ChunkCount = 1,
            StagingPath = stagingFile,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
        Environment.SetEnvironmentVariable("Storage__StagingReserveBytes", "1048576");
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

    private async Task ResetAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM upload_chunks; DELETE FROM upload_sessions; DELETE FROM files;";
        await command.ExecuteNonQueryAsync();
    }

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-cleanup-" + Guid.NewGuid().ToString("N"));

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
