using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Files.Application.Identity;
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
/// Table-driven authorization + IDOR matrix (NFR-005, I-01, E-01): every implemented route × actor
/// (anonymous / user-owner / user-other / auditor / admin / integration-owner / integration-other /
/// integration-without-scope) asserting the exact outcome. Denials follow ADR-0012/0014:
/// <list type="bullet">
/// <item>a foreign resource (another user's/integration's file, session, link) → uniform 404 (no
/// existence oracle);</item>
/// <item>insufficient role (user/auditor on an admin route) or scope (missing <c>file.*</c>) → 403;</item>
/// <item>admins and auditors never reach file content/download (D-19, FR-ADM-008).</item>
/// </list>
/// </summary>
[Collection(UploadCollectionDefinition.Name)]
public class AuthorizationMatrixTests(PostgresDatabaseFixture postgres, LocalStackS3Fixture localstack) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private readonly Dictionary<Actor, HttpClient> _clients = new();

    // Seeded identities and resources.
    private Guid _ownerObjectId, _otherObjectId, _auditorObjectId, _adminObjectId;
    private Guid _ownerUserId, _otherUserId;
    private Guid _integrationAId, _integrationBId;
    private Guid _ownerFileId, _foreignFileId, _integrationFileId;
    private Guid _ownerSessionId;
    private string _publicLinkToken = string.Empty, _publicLinkKey = string.Empty;

    private enum Actor
    {
        Anonymous,
        Owner,
        Other,
        Auditor,
        Admin,
        IntegrationOwner,
        IntegrationOther,
        IntegrationReadOnly,
    }

    public async Task InitializeAsync()
    {
        await ResetAsync();

        _ownerObjectId = Guid.NewGuid();
        _otherObjectId = Guid.NewGuid();
        _auditorObjectId = Guid.NewGuid();
        _adminObjectId = Guid.NewGuid();

        _factory = CreateFactory();
        _clients[Actor.Anonymous] = _factory.CreateClient();
        _clients[Actor.Owner] = BearerClient(_ownerObjectId);
        _clients[Actor.Other] = BearerClient(_otherObjectId);
        _clients[Actor.Auditor] = BearerClient(_auditorObjectId);
        _clients[Actor.Admin] = BearerClient(_adminObjectId);

        // Users with Files-managed roles (ADR-0014).
        _ownerUserId = await SeedUserAsync(_ownerObjectId, "Owner", UserRole.User);
        _otherUserId = await SeedUserAsync(_otherObjectId, "Other", UserRole.User);
        await SeedUserAsync(_auditorObjectId, "Auditor", UserRole.Auditor);
        await SeedUserAsync(_adminObjectId, "Admin", UserRole.Admin);

        // Two active integrations + one API key each (full scopes), plus a read-only key for A.
        _integrationAId = await SeedIntegrationAsync("matrix-a");
        _integrationBId = await SeedIntegrationAsync("matrix-b");
        _clients[Actor.IntegrationOwner] = ApiKeyClient(await SeedApiKeyAsync(_integrationAId, ["file.write", "file.read", "file.delete"]));
        _clients[Actor.IntegrationOther] = ApiKeyClient(await SeedApiKeyAsync(_integrationBId, ["file.write", "file.read", "file.delete"]));
        _clients[Actor.IntegrationReadOnly] = ApiKeyClient(await SeedApiKeyAsync(_integrationAId, ["file.read"]));

        // Files: owner's, foreign (other's), and integration A's.
        _ownerFileId = await SeedHumanFileAsync(_ownerUserId, "owner.txt");
        _foreignFileId = await SeedHumanFileAsync(_otherUserId, "foreign.txt");
        _integrationFileId = await SeedIntegrationFileAsync(_integrationAId, "integration.csv");

        // A 1-chunk upload session owned by the owner, plus an S3 object for each file so content
        // endpoints can stream.
        _ownerSessionId = await CreateSessionAsync(_clients[Actor.Owner], "session.txt", 100);
        await PutObjectAsync($"f/{_ownerFileId:D}", "owner-content");
        await PutObjectAsync($"f/{_integrationFileId:D}", "integration-content");

        // A public link + key (shown once) on the owner's file for the anonymous matrix.
        var link = await _clients[Actor.Owner].PostAsJsonAsync($"/v1/portal/files/{_ownerFileId}/links", new { accessMode = "public" });
        var created = await Read(link);
        _publicLinkToken = created.GetProperty("token").GetString()!;
        _publicLinkKey = created.GetProperty("downloadKey").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Matrix_asserts_expected_outcome_for_every_route_and_actor()
    {
        var failures = new List<string>();
        var rows = BuildRows();

        foreach (var row in rows)
        {
            try
            {
                var response = await SendAsync(row);
                var bodyText = await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(row.Expected, $"case '{row.Name}' ({row.Method} {row.Path}) body={bodyText}");
                if (row.Code is not null)
                {
                    JsonDocument.Parse(bodyText).RootElement.GetProperty("code").GetString()
                        .Should().Be(row.Code, $"case '{row.Name}' ({row.Method} {row.Path}) body={bodyText}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"case '{row.Name}' ({row.Method} {row.Path}): {ex.Message}");
            }
        }

        failures.Should().BeEmpty($"the authorization matrix must hold for every row:\n{string.Join("\n", failures)}");
    }

    // ------------------------------------------------------------------ table

    private List<MatrixRow> BuildRows()
    {
        var rows = new List<MatrixRow>();

        void Row(string name, Actor actor, HttpMethod method, string path, HttpStatusCode expected, string? code = null, object? body = null, IReadOnlyDictionary<string, string>? headers = null) =>
            rows.Add(new MatrixRow(name, method, path, () => _clients[actor], expected, code, body, headers));

        // --- identity ---------------------------------------------------------
        Row("/v1/me (anonymous)", Actor.Anonymous, HttpMethod.Get, "/v1/me", HttpStatusCode.Unauthorized, "invalid_token");
        Row("/v1/me (user)", Actor.Owner, HttpMethod.Get, "/v1/me", HttpStatusCode.OK);
        Row("/v1/me (auditor)", Actor.Auditor, HttpMethod.Get, "/v1/me", HttpStatusCode.OK);
        Row("/v1/me (admin)", Actor.Admin, HttpMethod.Get, "/v1/me", HttpStatusCode.OK);
        Row("/v1/me (integration)", Actor.IntegrationOwner, HttpMethod.Get, "/v1/me", HttpStatusCode.Unauthorized, "invalid_token");

        // --- portal files (owner-scoped; admin implies user, auditor does not) ---
        Row("list files (owner)", Actor.Owner, HttpMethod.Get, "/v1/portal/files", HttpStatusCode.OK);
        Row("list files (admin)", Actor.Admin, HttpMethod.Get, "/v1/portal/files", HttpStatusCode.OK);
        Row("list files (auditor)", Actor.Auditor, HttpMethod.Get, "/v1/portal/files", HttpStatusCode.Forbidden, "forbidden");
        Row("list files (anonymous)", Actor.Anonymous, HttpMethod.Get, "/v1/portal/files", HttpStatusCode.Unauthorized, "invalid_token");

        Row("get own file (owner)", Actor.Owner, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.OK);
        Row("get foreign file (other)", Actor.Other, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.NotFound, "file_not_found");
        Row("get foreign file (admin)", Actor.Admin, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.NotFound, "file_not_found");
        Row("get foreign file (auditor)", Actor.Auditor, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.Forbidden, "forbidden");
        Row("get file (anonymous)", Actor.Anonymous, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.Unauthorized, "invalid_token");

        Row("patch own file (owner)", Actor.Owner, HttpMethod.Patch, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.OK,
            body: new { fileName = "owner-renamed.txt" }, headers: new Dictionary<string, string> { ["If-Match"] = "\"1\"" });
        Row("patch foreign file (other)", Actor.Other, HttpMethod.Patch, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.NotFound, "file_not_found",
            body: new { fileName = "x.txt" }, headers: new Dictionary<string, string> { ["If-Match"] = "\"1\"" });
        Row("patch foreign file (auditor)", Actor.Auditor, HttpMethod.Patch, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.Forbidden, "forbidden",
            body: new { fileName = "x.txt" }, headers: new Dictionary<string, string> { ["If-Match"] = "\"1\"" });

        Row("download grant own file (owner)", Actor.Owner, HttpMethod.Post, $"/v1/portal/files/{_ownerFileId}/downloads", HttpStatusCode.Created);
        Row("download grant foreign file (other)", Actor.Other, HttpMethod.Post, $"/v1/portal/files/{_ownerFileId}/downloads", HttpStatusCode.NotFound, "file_not_found");
        Row("download grant foreign file (admin)", Actor.Admin, HttpMethod.Post, $"/v1/portal/files/{_ownerFileId}/downloads", HttpStatusCode.NotFound, "file_not_found");
        Row("download grant foreign file (auditor)", Actor.Auditor, HttpMethod.Post, $"/v1/portal/files/{_ownerFileId}/downloads", HttpStatusCode.Forbidden, "forbidden");

        Row("delete foreign file (other)", Actor.Other, HttpMethod.Delete, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.NotFound, "file_not_found");
        Row("delete foreign file (auditor)", Actor.Auditor, HttpMethod.Delete, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.Forbidden, "forbidden");

        // --- portal upload sessions -------------------------------------------
        Row("create session (owner)", Actor.Owner, HttpMethod.Post, "/v1/portal/upload-sessions", HttpStatusCode.Created,
            body: new { fileName = "new.txt", sizeBytes = 0, contentType = "text/plain" });
        Row("create session (auditor)", Actor.Auditor, HttpMethod.Post, "/v1/portal/upload-sessions", HttpStatusCode.Forbidden, "forbidden",
            body: new { fileName = "new.txt", sizeBytes = 0, contentType = "text/plain" });
        Row("create session (integration)", Actor.IntegrationOwner, HttpMethod.Post, "/v1/portal/upload-sessions", HttpStatusCode.Unauthorized, "invalid_token",
            body: new { fileName = "new.txt", sizeBytes = 0, contentType = "text/plain" });

        Row("session status (owner)", Actor.Owner, HttpMethod.Get, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.OK);
        Row("session status (other)", Actor.Other, HttpMethod.Get, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.NotFound, "upload_session_not_found");
        Row("session status (auditor)", Actor.Auditor, HttpMethod.Get, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.Forbidden, "forbidden");
        Row("session status (anonymous)", Actor.Anonymous, HttpMethod.Get, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.Unauthorized, "invalid_token");

        Row("chunk write (owner)", Actor.Owner, HttpMethod.Put, $"/v1/portal/upload-sessions/{_ownerSessionId}/chunks/1", HttpStatusCode.OK,
            body: new ByteArrayContent(new byte[100]));
        Row("chunk write (other)", Actor.Other, HttpMethod.Put, $"/v1/portal/upload-sessions/{_ownerSessionId}/chunks/1", HttpStatusCode.NotFound, "upload_session_not_found",
            body: new ByteArrayContent(new byte[100]));
        Row("chunk write (auditor)", Actor.Auditor, HttpMethod.Put, $"/v1/portal/upload-sessions/{_ownerSessionId}/chunks/1", HttpStatusCode.Forbidden, "forbidden",
            body: new ByteArrayContent(new byte[100]));

        Row("abort session (owner)", Actor.Owner, HttpMethod.Delete, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.NoContent);
        Row("abort session (other)", Actor.Other, HttpMethod.Delete, $"/v1/portal/upload-sessions/{_ownerSessionId}", HttpStatusCode.NotFound, "upload_session_not_found");

        // --- portal links (owner-scoped) --------------------------------------
        Row("list file links (owner)", Actor.Owner, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}/links", HttpStatusCode.OK);
        Row("list file links (foreign)", Actor.Other, HttpMethod.Get, $"/v1/portal/files/{_ownerFileId}/links", HttpStatusCode.NotFound);
        Row("list my links (owner)", Actor.Owner, HttpMethod.Get, "/v1/portal/links", HttpStatusCode.OK);
        Row("list my links (auditor)", Actor.Auditor, HttpMethod.Get, "/v1/portal/links", HttpStatusCode.Forbidden, "forbidden");

        // --- public links + grants (anonymous capability) ---------------------
        Row("public link metadata (anonymous)", Actor.Anonymous, HttpMethod.Get, $"/v1/public/download-links/{_publicLinkToken}", HttpStatusCode.OK);
        Row("public link redeem (anonymous, correct key)", Actor.Anonymous, HttpMethod.Post, $"/v1/public/download-links/{_publicLinkToken}/downloads", HttpStatusCode.Created,
            body: new { key = _publicLinkKey });
        Row("public link redeem (anonymous, wrong key)", Actor.Anonymous, HttpMethod.Post, $"/v1/public/download-links/{_publicLinkToken}/downloads", HttpStatusCode.NotFound,
            body: new { key = "ZZZZZZZZ" });
        Row("download grant stream (anonymous, invalid)", Actor.Anonymous, HttpMethod.Get, "/v1/download-grants/not-a-grant", HttpStatusCode.NotFound);

        // --- integration ------------------------------------------------------
        Row("integration me (owner)", Actor.IntegrationOwner, HttpMethod.Get, "/v1/integrations/me", HttpStatusCode.OK);
        Row("integration me (read-only key)", Actor.IntegrationReadOnly, HttpMethod.Get, "/v1/integrations/me", HttpStatusCode.OK);
        Row("integration me (anonymous)", Actor.Anonymous, HttpMethod.Get, "/v1/integrations/me", HttpStatusCode.Unauthorized, "invalid_api_key");
        Row("integration me (user)", Actor.Owner, HttpMethod.Get, "/v1/integrations/me", HttpStatusCode.Unauthorized, "invalid_api_key");

        Row("integration list files (owner)", Actor.IntegrationOwner, HttpMethod.Get, "/v1/integrations/files", HttpStatusCode.OK);
        Row("integration list files (read-only)", Actor.IntegrationReadOnly, HttpMethod.Get, "/v1/integrations/files", HttpStatusCode.OK);
        Row("integration list files (other)", Actor.IntegrationOther, HttpMethod.Get, "/v1/integrations/files", HttpStatusCode.OK);

        Row("integration get file (owner)", Actor.IntegrationOwner, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.OK);
        Row("integration get file (other)", Actor.IntegrationOther, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.NotFound, "file_not_found");
        Row("integration get file (read-only)", Actor.IntegrationReadOnly, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.OK);

        Row("integration availability (owner)", Actor.IntegrationOwner, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}/availability", HttpStatusCode.OK);
        Row("integration availability (other)", Actor.IntegrationOther, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}/availability", HttpStatusCode.NotFound, "file_not_found");

        Row("integration content (owner)", Actor.IntegrationOwner, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}/content", HttpStatusCode.OK);
        Row("integration content (other)", Actor.IntegrationOther, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}/content", HttpStatusCode.NotFound, "file_not_found");
        Row("integration content (read-only)", Actor.IntegrationReadOnly, HttpMethod.Get, $"/v1/integrations/files/{_integrationFileId}/content", HttpStatusCode.OK);

        Row("integration delete (owner)", Actor.IntegrationOwner, HttpMethod.Delete, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.NoContent);
        Row("integration delete (other)", Actor.IntegrationOther, HttpMethod.Delete, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.NotFound, "file_not_found");
        Row("integration delete (read-only, no file.delete)", Actor.IntegrationReadOnly, HttpMethod.Delete, $"/v1/integrations/files/{_integrationFileId}", HttpStatusCode.Forbidden, "insufficient_permission");

        Row("integration create upload session (write scope)", Actor.IntegrationOwner, HttpMethod.Post, "/v1/integrations/upload-sessions", HttpStatusCode.Created,
            body: new { fileName = "i.txt", sizeBytes = 0, contentType = "text/plain" });
        Row("integration create upload session (read-only)", Actor.IntegrationReadOnly, HttpMethod.Post, "/v1/integrations/upload-sessions", HttpStatusCode.Forbidden, "insufficient_permission",
            body: new { fileName = "i.txt", sizeBytes = 0, contentType = "text/plain" });
        Row("integration create upload session (user)", Actor.Owner, HttpMethod.Post, "/v1/integrations/upload-sessions", HttpStatusCode.Unauthorized, "invalid_token",
            body: new { fileName = "i.txt", sizeBytes = 0, contentType = "text/plain" });

        // --- admin (role-gated; 403 for user/auditor, 401 for anon/integration) ---
        AddAdminGetRows(rows, Row);
        AddAdminDenialRows(rows, Row);

        // Owner delete is destructive (it revokes links); keep it last so earlier rows still see
        // the file, its links, and its download grants.
        Row("delete own file (owner)", Actor.Owner, HttpMethod.Delete, $"/v1/portal/files/{_ownerFileId}", HttpStatusCode.NoContent);

        return rows;
    }

    private void AddAdminGetRows(List<MatrixRow> rows, Action<string, Actor, HttpMethod, string, HttpStatusCode, string?, object?, IReadOnlyDictionary<string, string>?> row)
    {
        var gets = new (string Name, string Path)[]
        {
            ("admin integrations", "/v1/admin/integrations"),
            ("admin integration detail", $"/v1/admin/integrations/{_integrationAId}"),
            ("admin integration api keys", $"/v1/admin/integrations/{_integrationAId}/api-keys"),
            ("admin users", "/v1/admin/users"),
            ("admin retention policy", "/v1/admin/retention-policy"),
            ("admin email templates", "/v1/admin/email-templates"),
        };
        foreach (var (name, path) in gets)
        {
            row($"{name} (admin)", Actor.Admin, HttpMethod.Get, path, HttpStatusCode.OK, null, null, null);
        }
    }

    private void AddAdminDenialRows(List<MatrixRow> rows, Action<string, Actor, HttpMethod, string, HttpStatusCode, string?, object?, IReadOnlyDictionary<string, string>?> row)
    {
        // Every admin route must reject a plain user/auditor with 403 and an unauthenticated caller
        // (or a machine key) with 401. The mutation bodies are irrelevant — authorization runs first.
        var routes = new (string Name, HttpMethod Method, string Path)[]
        {
            ("admin integrations", HttpMethod.Get, "/v1/admin/integrations"),
            ("admin create integration", HttpMethod.Post, "/v1/admin/integrations"),
            ("admin integration detail", HttpMethod.Get, $"/v1/admin/integrations/{_integrationAId}"),
            ("admin update integration", HttpMethod.Patch, $"/v1/admin/integrations/{_integrationAId}"),
            ("admin set permissions", HttpMethod.Put, $"/v1/admin/integrations/{_integrationAId}/permissions"),
            ("admin api keys", HttpMethod.Get, $"/v1/admin/integrations/{_integrationAId}/api-keys"),
            ("admin create api key", HttpMethod.Post, $"/v1/admin/integrations/{_integrationAId}/api-keys"),
            ("admin revoke api key", HttpMethod.Delete, $"/v1/admin/integrations/{_integrationAId}/api-keys/{Guid.NewGuid()}"),
            ("admin users", HttpMethod.Get, "/v1/admin/users"),
            ("admin change role", HttpMethod.Patch, $"/v1/admin/users/{_ownerUserId}/role"),
            ("admin retention policy", HttpMethod.Get, "/v1/admin/retention-policy"),
            ("admin create retention policy", HttpMethod.Post, "/v1/admin/retention-policy"),
            ("admin create retention migration", HttpMethod.Post, "/v1/admin/retention-migrations"),
            ("admin file retention override", HttpMethod.Post, $"/v1/admin/files/{_ownerFileId}/retention"),
            ("admin legal hold", HttpMethod.Post, $"/v1/admin/files/{_ownerFileId}/legal-hold"),
            ("admin notification policy", HttpMethod.Get, "/v1/admin/notification-policy"),
            ("admin update notification policy", HttpMethod.Put, "/v1/admin/notification-policy"),
            ("admin email templates", HttpMethod.Get, "/v1/admin/email-templates"),
            ("admin upsert email template", HttpMethod.Put, "/v1/admin/email-templates/expiry/en-US"),
        };

        foreach (var (name, method, path) in routes)
        {
            // Mutating routes take a JSON body; send an empty object so the endpoint (if it were
            // reached) has a bindable body. Authorization runs first, so the denial still wins.
            var body = method == HttpMethod.Get ? null : new { };
            row($"{name} (user)", Actor.Owner, method, path, HttpStatusCode.Forbidden, "forbidden", body, null);
            row($"{name} (auditor)", Actor.Auditor, method, path, HttpStatusCode.Forbidden, "forbidden", body, null);
            row($"{name} (anonymous)", Actor.Anonymous, method, path, HttpStatusCode.Unauthorized, "invalid_token", body, null);
            row($"{name} (integration)", Actor.IntegrationOwner, method, path, HttpStatusCode.Unauthorized, "invalid_token", body, null);
        }
    }

    // ------------------------------------------------------------------ helpers

    private sealed record MatrixRow(
        string Name,
        HttpMethod Method,
        string Path,
        Func<HttpClient> Client,
        HttpStatusCode Expected,
        string? Code,
        object? Body,
        IReadOnlyDictionary<string, string>? Headers);

    private static async Task<HttpResponseMessage> SendAsync(MatrixRow row)
    {
        var request = new HttpRequestMessage(row.Method, row.Path);
        if (row.Body is HttpContent content)
        {
            request.Content = content;
        }
        else if (row.Body is not null)
        {
            request.Content = JsonContent.Create(row.Body);
        }

        if (row.Headers is not null)
        {
            foreach (var (key, value) in row.Headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return await row.Client().SendAsync(request);
    }

    private HttpClient BearerClient(Guid objectId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthTestJwt.CreateUserToken(objectId));
        return client;
    }

    private HttpClient ApiKeyClient(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private async Task<Guid> SeedUserAsync(Guid objectId, string name, UserRole role)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var user = User.Create(AuthTestJwt.TenantId, objectId, name, $"{name.ToLowerInvariant()}@example.com");
        user.Role = role;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedIntegrationAsync(string slug)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var integration = Integration.Create($"{slug}-{Guid.NewGuid():N}"[..12], slug);
        foreach (var permission in new[] { "file.write", "file.read", "file.delete" })
        {
            integration.Permissions.Add(new IntegrationPermissionEntry { IntegrationId = integration.Id, Permission = permission });
        }

        db.Integrations.Add(integration);
        await db.SaveChangesAsync();
        return integration.Id;
    }

    private async Task<string> SeedApiKeyAsync(Guid integrationId, string[] scopes)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var prefix = ApiKeys.GeneratePrefix();
        var secret = ApiKeys.GenerateSecret();
        var fullKey = ApiKeys.Format(prefix, secret);
        db.IntegrationApiKeys.Add(new IntegrationApiKey
        {
            IntegrationId = integrationId,
            Name = "matrix",
            Prefix = prefix,
            SecretHash = ApiKeys.Hash(fullKey),
            Scopes = scopes,
        });
        await db.SaveChangesAsync();
        return fullKey;
    }

    private async Task<Guid> SeedHumanFileAsync(Guid ownerId, string name)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateHuman(
            ownerId, name, "files-local", string.Empty, 12, "text/plain",
            Retention.Finite(DateTimeOffset.UtcNow.AddDays(30), 30));
        file.StorageKey = $"f/{file.Id:D}";
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> SeedIntegrationFileAsync(Guid integrationId, string name)
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(postgres.ConnectionString));
        var file = FileEntity.CreateIntegration(integrationId, name, "files-local", string.Empty, 20, "text/csv");
        file.StorageKey = $"f/{file.Id:D}";
        file.Status = FileStatus.Available;
        file.ScanStatus = ScanStatus.Clean;
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, string name, int sizeBytes)
    {
        var create = await client.PostAsJsonAsync("/v1/portal/upload-sessions", new
        {
            fileName = name,
            sizeBytes,
            contentType = "text/plain",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Read(create)).GetProperty("uploadSessionId").GetGuid();
    }

    private async Task PutObjectAsync(string key, string content)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            UseHttp = true,
            AuthenticationRegion = LocalStackS3Fixture.Region,
            ServiceURL = localstack.ServiceUrl,
        };
        using var client = new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);
        using var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = LocalStackS3Fixture.Bucket,
            Key = key,
            InputStream = body,
        });
    }

    private async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE download_key_attempts, abuse_lockouts, download_events, download_grants,
                download_links, download_link_acl, file_tags, files, tags, upload_chunks,
                upload_sessions, integration_file_grants, integration_api_keys,
                integration_permissions, integrations, users, retention_migrations,
                retention_policies, notification_events, notification_policies, email_templates,
                admin_audit_events, file_audit_events, outbox_messages, idempotency_records,
                daily_usage
            RESTART IDENTITY CASCADE;
            """;
        await command.ExecuteNonQueryAsync();
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
