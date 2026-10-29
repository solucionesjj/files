using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Application.Lifecycle;
using Files.Application.Notifications;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Identity;
using Files.Infrastructure.Persistence;
using Files.IntegrationTests.Auth;
using Files.IntegrationTests.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Files.IntegrationTests.RetentionApi;

/// <summary>
/// S10/M6 integration coverage: retention policy versioning, legal hold, bulk migration,
/// notification scheduling/dedupe/supersede, SMTP delivery via Mailpit, and retry/attempt states.
/// Uses the real API host (WebApplicationFactory) plus direct worker-service invocation.
/// </summary>
[Collection(RetentionCollectionDefinition.Name)]
public class RetentionNotificationsTests(PostgresDatabaseFixture postgres, MailpitFixture mailpit)
{
    // ------------------------------------------------------------------ admin: retention policy

    [Fact]
    public async Task Publish_retention_policy_versions_and_return_history()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var token = await SeedAdminAsync();
            using var client = AdminClient(factory, token);

            var before = await client.GetAsync("/v1/admin/retention-policy");
            before.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(before)).GetProperty("current").GetProperty("defaultDays").GetInt32().Should().Be(30);

            var created = await client.PostAsJsonAsync("/v1/admin/retention-policy", new
            {
                defaultDays = 60,
                maxDays = 365,
                allowUserOverride = true,
                allowIndefinite = false,
                publicLinksEnabled = true,
                notes = "60-day default",
                reason = "aligned with legal retention schedule",
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var createdBody = await Read(created);
            createdBody.GetProperty("defaultDays").GetInt32().Should().Be(60);
            createdBody.GetProperty("isActive").GetBoolean().Should().BeTrue();

            var after = await client.GetAsync("/v1/admin/retention-policy");
            var afterBody = await Read(after);
            afterBody.GetProperty("current").GetProperty("defaultDays").GetInt32().Should().Be(60);
            afterBody.GetProperty("history").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM retention_policies WHERE is_active";
            ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(1);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Legal_hold_blocks_expiration_until_released()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var token = await SeedAdminAsync();
            using var client = AdminClient(factory, token);

            var userId = await SeedUserAsync("owner@test.local");
            var fileId = await SeedFileAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

            var hold = await client.PostAsJsonAsync($"/v1/admin/files/{fileId}/legal-hold", new
            {
                enabled = true,
                reason = "pending litigation hold",
                reviewAt = (DateTimeOffset?)null,
            });
            hold.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(hold)).GetProperty("legalHold").GetBoolean().Should().BeTrue();

            await RunAsync<IExpirationService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("available");

            var release = await client.PostAsJsonAsync($"/v1/admin/files/{fileId}/legal-hold", new
            {
                enabled = false,
                reason = "litigation hold released",
                reviewAt = (DateTimeOffset?)null,
            });
            release.StatusCode.Should().Be(HttpStatusCode.OK);

            await RunAsync<IExpirationService>(factory, s => s.RunOnceAsync());

            (await StatusOfAsync(fileId)).Should().Be("pending_deletion");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Bulk_retention_migration_applies_policy_and_recalculates_notifications()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var token = await SeedAdminAsync();
            using var client = AdminClient(factory, token);

            var userId = await SeedUserAsync("migration@test.local");
            var fileA = await SeedFileAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddDays(90));
            var fileB = await SeedFileAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddDays(90));
            var integrationFile = await SeedIntegrationFileAsync();

            // Schedule notifications for the human files first so we can observe supersede.
            await RunAsync<INotificationScheduler>(factory, s => s.RunOnceAsync());
            (await CountAsync("notification_events")).Should().Be(6);

            var policy = await client.PostAsJsonAsync("/v1/admin/retention-policy", new
            {
                defaultDays = 45,
                maxDays = 365,
                allowUserOverride = true,
                allowIndefinite = false,
                publicLinksEnabled = true,
                reason = "migration target policy",
            });
            var targetPolicyId = (await Read(policy)).GetProperty("id").GetGuid();

            var migration = await client.PostAsJsonAsync("/v1/admin/retention-migrations", new
            {
                targetPolicyId,
                reason = "apply 45-day default to all files",
            });
            migration.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var migrationId = (await Read(migration)).GetProperty("id").GetGuid();

            await RunAsync<IRetentionMigrationService>(factory, s => s.RunOnceAsync());

            var migrationStatus = await client.GetAsync($"/v1/admin/retention-migrations/{migrationId}");
            migrationStatus.StatusCode.Should().Be(HttpStatusCode.OK);
            var migrationBody = await Read(migrationStatus);
            migrationBody.GetProperty("status").GetString().Should().Be("completed", because: migrationBody.GetProperty("lastError").GetString());

            (await RetentionDaysOfAsync(fileA)).Should().Be(45);
            (await RetentionDaysOfAsync(fileB)).Should().Be(45);
            (await RetentionDaysOfAsync(integrationFile)).Should().BeNull(); // integration skipped

            // Old 90-day events superseded; new 45-day events scheduled (3 intervals × 2 files).
            (await CountWhereAsync("notification_events", "status", "superseded")).Should().Be(6);
            (await CountWhereAsync("notification_events", "status", "scheduled")).Should().Be(6);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Notification_scheduling_is_idempotent_and_sends_via_smtp()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);

            var userId = await SeedUserAsync("notify@test.local");
            var fileId = await SeedFileAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddDays(10));

            await RunAsync<INotificationScheduler>(factory, s => s.RunOnceAsync());
            (await CountAsync("notification_events")).Should().Be(3);

            await RunAsync<INotificationScheduler>(factory, s => s.RunOnceAsync());
            (await CountAsync("notification_events")).Should().Be(3); // dedupe

            await MakeDueAsync(fileId);

            await RunAsync<INotificationSender>(factory, s => s.RunOnceAsync());

            (await CountWhereAsync("notification_events", "status", "accepted")).Should().Be(3);
            (await CountAsync("notification_delivery_attempts")).Should().Be(3);

            var messages = await mailpit.GetMessagesAsync();
            messages.Should().HaveCount(3);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Notification_send_failure_retries_with_backoff()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging, new Dictionary<string, string>
            {
                ["Smtp__Host"] = "127.0.0.1",
                ["Smtp__Port"] = "1", // closed port → transient failure
                ["Smtp__UseStartTls"] = "false",
            });

            var userId = await SeedUserAsync("retry@test.local");
            var fileId = await SeedFileAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddDays(10));

            await RunAsync<INotificationScheduler>(factory, s => s.RunOnceAsync());
            await MakeDueAsync(fileId);

            await RunAsync<INotificationSender>(factory, s => s.RunOnceAsync());
            (await CountWhereAsync("notification_events", "status", "failed")).Should().Be(3);
            (await AttemptsOfAsync(fileId, 7)).Should().Be(1);

            await MakeDueAsync(fileId);
            await RunAsync<INotificationSender>(factory, s => s.RunOnceAsync());
            (await AttemptsOfAsync(fileId, 7)).Should().Be(2);

            (await CountAsync("notification_delivery_attempts")).Should().Be(6); // 2 attempts × 3 events
        }
        finally
        {
            TryDelete(staging);
        }
    }

    [Fact]
    public async Task Admin_notification_policy_and_email_templates_are_versioned()
    {
        var staging = NewStagingPath();
        try
        {
            await ResetStateAsync();
            await using var factory = CreateFactory(staging);
            var token = await SeedAdminAsync();
            using var client = AdminClient(factory, token);

            var policy = await client.PutAsJsonAsync("/v1/admin/notification-policy", new
            {
                name = "Weekly + daily",
                intervalsDays = new[] { 14, 1 },
                templateKey = "file.expiring",
                reason = "changed reminder cadence",
            });
            policy.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(policy)).GetProperty("intervalsDays").EnumerateArray().Select(x => x.GetInt32())
                .Should().BeEquivalentTo(new[] { 14, 1 });

            var template = await client.PutAsJsonAsync("/v1/admin/email-templates/file.expiring/es-CO", new
            {
                subject = "Recordatorio actualizado {{fileName}}",
                bodyHtml = "<p>{{ownerName}} {{fileName}} {{expiresAt}}</p>",
                bodyText = "{{ownerName}} {{fileName}} {{expiresAt}}",
                reason = "updated legal wording",
            });
            template.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(template)).GetProperty("version").GetInt32().Should().Be(2);

            var list = await client.GetAsync("/v1/admin/email-templates");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Read(list)).GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Guid> SeedUserAsync(string email)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var user = User.Create(AuthTestJwt.TenantId, Guid.NewGuid(), "Owner " + email, email);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedFileAsync(Guid ownerUserId, DateTimeOffset expiresAt, int retentionDays = 30)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(ownerUserId, "report.pdf", "files-local", $"f/{Guid.NewGuid():N}", 1234, "application/pdf", Files.Domain.ValueObjects.Retention.Finite(expiresAt, retentionDays));
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        file.AvailableAt = DateTimeOffset.UtcNow;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> SeedIntegrationFileAsync()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var integration = Integration.Create("it-" + Guid.NewGuid().ToString("N")[..8], "Migration Integration");
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var file = FileEntity.CreateIntegration(integration.Id, "machine.bin", "files-local", $"f/{Guid.NewGuid():N}", 999, "application/octet-stream");
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        file.AvailableAt = DateTimeOffset.UtcNow;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<string> SeedAdminAsync()
    {
        var objectId = Guid.NewGuid();
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var admin = User.Create(AuthTestJwt.TenantId, objectId, "Files Admin", "admin@test.local");
        admin.Role = UserRole.Admin;
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return AuthTestJwt.CreateUserToken(objectId);
    }

    private static async Task RunAsync<TInterface>(WebApplicationFactory<Program> factory, Func<TInterface, Task> run)
        where TInterface : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TInterface>();
        await run(service);
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task MakeDueAsync(Guid fileId)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE notification_events SET next_attempt_at = now() - interval '1 minute', updated_at = now() WHERE file_id = @fileId AND status IN ('scheduled', 'failed')";
        cmd.Parameters.AddWithValue("fileId", fileId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> StatusOfAsync(Guid fileId)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT status FROM files WHERE id = @fileId";
        cmd.Parameters.AddWithValue("fileId", fileId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int?> RetentionDaysOfAsync(Guid fileId)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT retention_days FROM files WHERE id = @fileId";
        cmd.Parameters.AddWithValue("fileId", fileId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (int)result;
    }

    private async Task<int> AttemptsOfAsync(Guid fileId, int intervalDays)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT attempts FROM notification_events WHERE file_id = @fileId AND interval_days = @interval";
        cmd.Parameters.AddWithValue("fileId", fileId);
        cmd.Parameters.AddWithValue("interval", intervalDays);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountWhereAsync(string table, string column, object value)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table} WHERE {column} = @value";
        cmd.Parameters.AddWithValue("value", value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        connection.Open();
        return connection;
    }

    private WebApplicationFactory<Program> CreateFactory(string stagingPath, Dictionary<string, string>? overrides = null)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", stagingPath);
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "retention-test-pepper");
        Environment.SetEnvironmentVariable("Features__MalwareScanning", "false");
        Environment.SetEnvironmentVariable("Smtp__Host", "127.0.0.1");
        Environment.SetEnvironmentVariable("Smtp__Port", mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("Smtp__UseStartTls", "false");
        Environment.SetEnvironmentVariable("Notifications__FromAddress", "files@test.local");
        Environment.SetEnvironmentVariable("Entra__TenantId", AuthTestJwt.TenantId.ToString());
        Environment.SetEnvironmentVariable("Entra__ApiAudience", AuthTestJwt.Audience);
        Environment.SetEnvironmentVariable("Entra__ApiClientId", AuthTestJwt.ApiClientId.ToString());
        Environment.SetEnvironmentVariable("Entra__IssuerSigningKey", AuthTestJwt.PublicKeyBase64);
        Environment.SetEnvironmentVariable("S3__Region", "us-east-1");
        Environment.SetEnvironmentVariable("S3__Bucket", "files-local");
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

    private async Task ResetStateAsync()
    {
        await using var connection = Open();
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

            -- Restore the seeded policy/template baseline: drop any version a prior test published
            -- and re-activate the seeded rows (created_by IS NULL) so each test starts deterministic.
            UPDATE retention_policies SET is_active = false WHERE is_active;
            UPDATE notification_policies SET is_active = false WHERE is_active;
            UPDATE email_templates SET is_active = false WHERE is_active;

            DELETE FROM retention_policies WHERE created_by IS NOT NULL;
            DELETE FROM notification_policies WHERE created_by IS NOT NULL;
            DELETE FROM email_templates WHERE created_by IS NOT NULL;

            UPDATE retention_policies SET is_active = true WHERE created_by IS NULL;
            UPDATE notification_policies SET is_active = true WHERE created_by IS NULL;
            UPDATE email_templates SET is_active = true WHERE created_by IS NULL;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    private static string NewStagingPath() =>
        Path.Combine(Path.GetTempPath(), "files-retention-" + Guid.NewGuid().ToString("N"));

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
