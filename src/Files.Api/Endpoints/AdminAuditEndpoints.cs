using System.Text.Json;
using Files.Api.Authentication;
using Files.Application.Abstractions.Repositories;
using Files.Application.Admin;
using Files.Application.Files;

namespace Files.Api.Endpoints;

/// <summary>
/// Read-only admin/auditor surface (FR-ADM-005/006/008, FR-AUD-001..003): audit events, download
/// events, aggregate usage, and platform settings. Admins and auditors may read these; neither may
/// read file content (D-19). Every resource is append-only by declaration — no mutating operation is
/// mapped here.
/// </summary>
public static class AdminAuditEndpoints
{
    public static void MapAdminAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/audit-events", ListAuditEventsAsync)
            .RequireAuthorization(AuthenticationSetup.AdminOrAuditorPolicy);
        endpoints.MapGet("/v1/admin/download-events", ListDownloadEventsAsync)
            .RequireAuthorization(AuthenticationSetup.AdminOrAuditorPolicy);
        endpoints.MapGet("/v1/admin/usage", GetUsageAsync)
            .RequireAuthorization(AuthenticationSetup.AdminOrAuditorPolicy);
        endpoints.MapGet("/v1/admin/settings", GetSettingsAsync)
            .RequireAuthorization(AuthenticationSetup.AdminOrAuditorPolicy);
    }

    private static async Task<IResult> ListAuditEventsAsync(
        HttpContext context,
        IAuditLogRepository auditLog,
        string? category,
        Guid? fileId,
        string? actorType,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        if (category is not null && category is not "file" and not "admin")
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "category must be 'file' or 'admin'");
        }

        if (actorType is not null && actorType is not "user" and not "integration" and not "admin" and not "system")
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "actorType must be one of: user, integration, admin, system");
        }

        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        if (!AdminAuditQueryParsing.TryDecodeCursor(cursor, out var decoded))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var query = new AuditEventQuery(category, fileId, actorType, action, from, to, decoded);
        var rows = await auditLog.ListAsync(query, pageLimit + 1, ct);
        var hasMore = rows.Count > pageLimit;
        var page = rows.Take(pageLimit).ToList();

        return Results.Ok(new AuditEventListResponse(
            page.Select(ToAuditEventResponse).ToArray(),
            hasMore && page.Count > 0 ? AdminAuditQueryParsing.EncodeCursor(new AuditEventCursor(page[^1].OccurredAt, page[^1].Id)) : null,
            hasMore));
    }

    private static async Task<IResult> ListDownloadEventsAsync(
        HttpContext context,
        IDownloadEventRepository downloadEvents,
        Guid? fileId,
        Guid? linkId,
        string? result,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        if (result is not null && result is not "authorized" and not "denied" and not "completed" and not "failed")
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "result must be one of: authorized, denied, completed, failed");
        }

        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        if (!AdminAuditQueryParsing.TryDecodeCursor(cursor, out var decoded))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var query = new DownloadEventQuery(fileId, linkId, result, from, to, decoded);
        var rows = await downloadEvents.ListAsync(query, pageLimit + 1, ct);
        var hasMore = rows.Count > pageLimit;
        var page = rows.Take(pageLimit).ToList();

        return Results.Ok(new DownloadEventListResponse(
            page.Select(ToDownloadEventResponse).ToArray(),
            hasMore && page.Count > 0 ? AdminAuditQueryParsing.EncodeCursor(new AuditEventCursor(page[^1].OccurredAt, page[^1].Id)) : null,
            hasMore));
    }

    private static async Task<IResult> GetUsageAsync(
        IUsageRepository usage,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var stats = await usage.GetAsync(ct);
        return Results.Ok(new UsageStatsResponse(
            timeProvider.GetUtcNow(),
            new UsageStorageResponse(
                stats.StorageTotalBytes,
                new UsageStorageByOwnerTypeResponse(stats.StorageHumanBytes, stats.StorageIntegrationBytes)),
            new UsageFilesResponse(stats.FilesTotal, stats.FilesAvailable, stats.FilesExpired, stats.FilesPendingDeletion),
            new UsageDownloadsResponse(stats.DownloadsLast30Days, stats.DownloadsLast7Days),
            stats.Integrations.Select(i => new UsageIntegrationResponse(i.IntegrationId, i.Name, i.Files, i.Bytes)).ToArray()));
    }

    private static async Task<IResult> GetSettingsAsync(
        IPlatformSettingsRepository settings,
        CancellationToken ct)
    {
        var rows = await settings.GetAllAsync(ct);
        var dictionary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in rows)
        {
            dictionary[key] = ParseJsonValue(value);
        }

        return Results.Ok(new PlatformSettingsResponse(dictionary));
    }

    private static AuditEventResponse ToAuditEventResponse(AuditEventRecord record) => new(
        record.Id,
        record.Category,
        record.FileId,
        record.ActorType,
        record.ActorId,
        record.Action,
        record.Result,
        record.Reason,
        ParseJsonValue(record.Metadata ?? "{}"),
        record.TraceId,
        record.OccurredAt);

    private static DownloadEventResponse ToDownloadEventResponse(DownloadEventRecord record) => new(
        record.Id,
        record.FileId,
        record.LinkId,
        record.ActorType,
        record.ActorId,
        record.Source,
        record.Result,
        record.ReasonCode,
        record.BytesSent,
        record.TraceId,
        record.OccurredAt);

    private static JsonElement ParseJsonValue(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}

public sealed record AuditEventResponse(
    Guid Id,
    string Category,
    Guid? FileId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string Result,
    string? Reason,
    JsonElement Metadata,
    string? TraceId,
    DateTimeOffset OccurredAt);

public sealed record AuditEventListResponse(
    IReadOnlyList<AuditEventResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record DownloadEventListResponse(
    IReadOnlyList<DownloadEventResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record UsageStorageByOwnerTypeResponse(long Human, long Integration);

public sealed record UsageStorageResponse(long TotalBytes, UsageStorageByOwnerTypeResponse ByOwnerType);

public sealed record UsageFilesResponse(long Total, long Available, long Expired, long PendingDeletion);

public sealed record UsageDownloadsResponse(long Last30Days, long Last7Days);

public sealed record UsageIntegrationResponse(Guid IntegrationId, string Name, long Files, long Bytes);

public sealed record UsageStatsResponse(
    DateTimeOffset GeneratedAt,
    UsageStorageResponse Storage,
    UsageFilesResponse Files,
    UsageDownloadsResponse Downloads,
    IReadOnlyList<UsageIntegrationResponse> Integrations);

public sealed record PlatformSettingsResponse(IReadOnlyDictionary<string, JsonElement> Settings);
