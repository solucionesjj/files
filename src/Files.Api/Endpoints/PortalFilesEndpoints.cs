using System.Globalization;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Application.Identity;
using Files.Application.Upload;
using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

/// <summary>Contract-first owner-scoped portal file endpoints (list/get/update/delete/download).</summary>
public static class PortalFilesEndpoints
{
    public static void MapPortalFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/portal/files", ListAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapGet("/v1/portal/files/{fileId:guid}", GetAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapPatch("/v1/portal/files/{fileId:guid}", UpdateAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapDelete("/v1/portal/files/{fileId:guid}", DeleteAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapPost("/v1/portal/files/{fileId:guid}/downloads", CreateDownloadGrantAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IIdentityService identityService,
        IFileRepository files,
        ITagRepository tags,
        string? q,
        string[]? tag,
        string[]? status,
        string? sort,
        int? limit,
        int? expiringWithinDays,
        DateTimeOffset? expiresBefore,
        DateTimeOffset? expiresAfter,
        string? cursor,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!FileQueryParsing.TryParseSort(sort, out var sortOrder))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid sort");
        }

        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        if (expiringWithinDays is < 1 or > 365)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "expiringWithinDays must be between 1 and 365");
        }

        if (!TryParseStatuses(status, out var statuses))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid status");
        }

        if (!FileQueryParsing.TryDecodeCursor(cursor, out var decodedCursor))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var query = new FileListQuery(
            string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            (tag ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray(),
            statuses,
            sortOrder,
            expiresBefore,
            expiresAfter,
            expiringWithinDays);

        var rows = await files.ListOwnedAsync(actor.User.Id, query, decodedCursor, pageLimit + 1, ct);
        var hasMore = rows.Count > pageLimit;
        var page = hasMore ? rows.Take(pageLimit).ToList() : rows.ToList();

        var tagMap = await tags.ListTagsByFileIdsAsync(page.Select(f => f.Id).ToList(), ct);
        var items = page
            .Select(f => FileMetadataMapper.ToResponse(f, tagMap.TryGetValue(f.Id, out var names) ? names : []))
            .ToArray();

        return Results.Ok(new FileListResponse(
            items,
            hasMore && page.Count > 0 ? FileQueryParsing.EncodeCursor(ToCursor(page[^1])) : null,
            hasMore));
    }

    private static async Task<IResult> GetAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IFileRepository files,
        ITagRepository tags,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var file = await files.GetOwnedFileAsync(fileId, actor.User.Id, ct);
        if (file is null)
        {
            return Problems.Response(context, StatusCodes.Status404NotFound, "file_not_found", "File not found");
        }

        context.Response.Headers.ETag = $"\"{file.Version}\"";
        return Results.Ok(FileMetadataMapper.ToResponse(file, await tags.ListFileTagsAsync(fileId, ct)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid fileId,
        UpdateFileRequest request,
        HttpContext context,
        IIdentityService identityService,
        IFileManagementService files,
        ITagRepository tags,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!TryParseIfMatch(context.Request.Headers["If-Match"].FirstOrDefault(), out var ifMatch))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "If-Match header is required and must be a version");
        }

        var result = await files.UpdateAsync(
            fileId,
            actor.User.Id,
            new UpdateFileCommand(
                request.FileName,
                request.Retention is null ? null : new RetentionInput(request.Retention.Mode, request.Retention.Days, request.Retention.Reason),
                request.Tags),
            ifMatch,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        var file = result.Value!;
        context.Response.Headers.ETag = $"\"{file.Version}\"";
        return Results.Ok(FileMetadataMapper.ToResponse(file, await tags.ListFileTagsAsync(fileId, ct)));
    }

    private static async Task<IResult> DeleteAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IFileManagementService files,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await files.DeleteAsync(
            fileId,
            actor.User.Id,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        return result.IsSuccess ? Results.NoContent() : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> CreateDownloadGrantAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IDownloadGrantService grants,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await grants.IssueOwnerAsync(fileId, actor.User.Id, RequestContext.IpHash(context), ct);
        return result.IsSuccess
            ? Results.Created($"/v1/download-grants/{result.Value!.GrantToken}", result.Value)
            : ToProblem(context, result.Error!);
    }

    private static bool TryParseIfMatch(string? value, out int? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Trim('"');
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static IResult ToProblem(HttpContext context, ServiceError error) =>
        Problems.Response(context, error.Status, error.Code, error.Title, error.Detail);

    private static bool TryParseStatuses(string[]? values, out IReadOnlyList<FileStatus>? statuses)
    {
        statuses = null;
        if (values is not { Length: > 0 })
        {
            return true;
        }

        var parsed = new List<FileStatus>(values.Length);
        foreach (var value in values)
        {
            if (!FileQueryParsing.TryParseStatus(value, out var parsedStatus))
            {
                statuses = null;
                return false;
            }

            parsed.Add(parsedStatus);
        }

        statuses = parsed;
        return true;
    }

    private static FileListCursor ToCursor(FileEntity file) =>
        new(file.CreatedAt, file.OriginalFilename, file.SuccessfulDownloads, file.Id);
}

public sealed record UpdateFileRequest(string? FileName, RetentionRequest? Retention, IReadOnlyList<string>? Tags);

public sealed record FileListResponse(
    IReadOnlyList<FileMetadataResponse> Items,
    string? NextCursor,
    bool HasMore);
