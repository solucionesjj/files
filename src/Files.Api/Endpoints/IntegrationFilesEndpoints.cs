using Files.Api.Authentication;
using Files.Api.Middleware;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Application.Identity;
using Files.Application.Integrations;
using Files.Application.Storage;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Files.Api.Endpoints;

/// <summary>
/// Contract-first integration file endpoints (S9 / FR-INT-002/005/008/009, FR-M2M-007/011):
/// owner-scoped list, owned-or-granted metadata, owner-only logical delete, a lightweight
/// availability check, and streamed content with single-range support, counted only on completion.
/// </summary>
public static partial class IntegrationFilesEndpoints
{
    public static void MapIntegrationFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/integrations/files", ListAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationReadPolicy);
        endpoints.MapGet("/v1/integrations/files/{fileId:guid}", GetAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationReadPolicy);
        endpoints.MapDelete("/v1/integrations/files/{fileId:guid}", DeleteAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationDeletePolicy)
            .WithMetadata(new IdempotentAttribute());
        endpoints.MapGet("/v1/integrations/files/{fileId:guid}/availability", AvailabilityAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationReadPolicy);
        endpoints.MapGet("/v1/integrations/files/{fileId:guid}/content", DownloadAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationReadPolicy);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IIdentityService identityService,
        IIntegrationFileService service,
        string[]? status,
        int? limit,
        string? cursor,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!IsIntegration(actor, context, out var integrationId))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        if (!TryParseStatuses(status, out var statuses))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid status");
        }

        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        if (!IntegrationFileCursorParsing.TryDecode(cursor, out var decodedCursor))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var result = await service.ListAsync(integrationId, statuses, decodedCursor, pageLimit + 1, ct);
        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        var rows = result.Value!;
        var hasMore = rows.Count > pageLimit;
        var page = hasMore ? rows.Take(pageLimit).ToList() : rows.ToList();
        var items = page.Select(IntegrationFileMapper.ToResponse).ToArray();

        return Results.Ok(new IntegrationFileListResponse(
            items,
            hasMore && page.Count > 0 ? IntegrationFileCursorParsing.Encode(ToCursor(page[^1])) : null,
            hasMore));
    }

    private static async Task<IResult> GetAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationFileService service,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!IsIntegration(actor, context, out var integrationId))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        var result = await service.GetAsync(fileId, integrationId, ct);
        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        var file = result.Value!;
        context.Response.Headers.ETag = $"\"{file.Version}\"";
        return Results.Ok(IntegrationFileMapper.ToResponse(file));
    }

    private static async Task<IResult> DeleteAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationFileService service,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!IsIntegration(actor, context, out var integrationId))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        var result = await service.DeleteAsync(
            fileId,
            integrationId,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        return result.IsSuccess ? Results.NoContent() : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> AvailabilityAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationFileService service,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!IsIntegration(actor, context, out var integrationId))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        var result = await service.GetAvailabilityAsync(fileId, integrationId, ct);
        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        var view = result.Value!;
        return Results.Ok(new AvailabilityResponse(
            view.FileId,
            view.Available ? "available" : "unavailable",
            view.SizeBytes,
            view.ExpiresAt,
            ScanStatusName(view.ScanStatus),
            view.CheckedAt));
    }

    private static async Task<IResult> DownloadAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationFileService service,
        IIntegrationQuotaService quota,
        IIntegrationFileRepository integrationFiles,
        IObjectStore objectStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Files.Api.IntegrationDownload");

        var actor = await identityService.ResolveAsync(ct);
        if (!IsIntegration(actor, context, out var integrationId))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        var file = await integrationFiles.GetAccessibleAsync(fileId, integrationId, ct);
        if (file is null)
        {
            await RecordDeniedAsync(unitOfWork, integrationFiles, null, integrationId, "not_authorized", ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "file_not_found", "File not found");
        }

        if (!FileDownloadability.IsDownloadable(file, timeProvider.GetUtcNow()))
        {
            await RecordDeniedAsync(unitOfWork, integrationFiles, file.Id, integrationId, "file_unavailable", ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "file_unavailable", "File is not available for download");
        }

        var rangeHeader = context.Request.Headers["Range"].FirstOrDefault();
        var parse = HttpRange.Parse(rangeHeader, file.SizeBytes, out var offset, out var length);
        if (parse == ByteRangeParse.Unsatisfiable)
        {
            context.Response.Headers["Content-Range"] = $"bytes */{file.SizeBytes}";
            return Problems.Response(context, StatusCodes.Status416RangeNotSatisfiable, "range_not_satisfiable", "Requested range is not satisfiable");
        }

        var quotaError = await quota.CheckDownloadAsync(integrationId, length, ct);
        if (quotaError is not null)
        {
            return ToProblem(context, quotaError);
        }

        Stream stream;
        try
        {
            stream = parse == ByteRangeParse.Satisfiable
                ? await objectStore.GetObjectAsync(file.StorageKey!, offset, length, ct)
                : await objectStore.GetObjectAsync(file.StorageKey!, ct);
        }
        catch (ObjectMissingException)
        {
            await RecordDeniedAsync(unitOfWork, integrationFiles, file.Id, integrationId, "object_missing", ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "file_unavailable", "File is not available for download");
        }

        context.Response.Headers["Accept-Ranges"] = "bytes";
        context.Response.Headers["Content-Disposition"] = ContentDisposition.Build(file.OriginalFilename);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.ContentType = file.ContentType;

        if (parse == ByteRangeParse.Satisfiable)
        {
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            context.Response.ContentLength = length;
            context.Response.Headers["Content-Range"] = $"bytes {offset}-{offset + length - 1}/{file.SizeBytes}";
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentLength = file.SizeBytes;
        }

        var completed = false;
        long bytesSent = 0;
        try
        {
            bytesSent = await CopyToAsync(stream, context.Response.Body, ct);
            completed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected; an incomplete stream is never counted (ADR-0005).
        }
        finally
        {
            await stream.DisposeAsync();
        }

        if (completed)
        {
            try
            {
                await unitOfWork.BeginTransactionAsync(ct);
                await integrationFiles.RecordCompletedDownloadAsync(file.Id, integrationId, bytesSent, ct);
                await quota.RecordDownloadedAsync(integrationId, bytesSent, ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bytes were delivered but the count/usage write failed; accepted gap (ADR-0005).
                try
                {
                    await unitOfWork.RollbackAsync(ct);
                }
                catch (Exception)
                {
                }

                Log.CompletionFailed(logger, file.Id, ex);
            }
        }
        else
        {
            await RecordDeniedAsync(unitOfWork, integrationFiles, file.Id, integrationId, "client_disconnect", ct);
        }

        return Results.Empty;
    }

    private static async Task RecordDeniedAsync(
        IUnitOfWork unitOfWork,
        IIntegrationFileRepository integrationFiles,
        Guid? fileId,
        Guid integrationId,
        string reasonCode,
        CancellationToken ct)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            await integrationFiles.RecordDownloadEventAsync(fileId, integrationId, "denied", reasonCode, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch (Exception)
        {
            try
            {
                await unitOfWork.RollbackAsync(ct);
            }
            catch (Exception)
            {
            }
        }
    }

    private static async Task<long> CopyToAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }

        return total;
    }

    private static bool IsIntegration(ResolvedActor actor, HttpContext context, out Guid integrationId)
    {
        integrationId = actor.Id ?? Guid.Empty;
        return actor.Type == ActorType.Integration && actor.Id is not null;
    }

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
                return false;
            }

            parsed.Add(parsedStatus);
        }

        statuses = parsed;
        return true;
    }

    private static IntegrationFileCursor ToCursor(FileEntity file) => new(file.CreatedAt, file.Id);

    private static string ScanStatusName(ScanStatus status) => status switch
    {
        ScanStatus.NotScanned => "not_scanned",
        ScanStatus.Pending => "pending",
        ScanStatus.Clean => "clean",
        ScanStatus.Infected => "infected",
        ScanStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static IResult ToProblem(HttpContext context, ServiceError error) =>
        Problems.Response(context, error.Status, error.Code, error.Title, error.Detail);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record completed integration download for file {FileId}")]
        public static partial void CompletionFailed(ILogger logger, Guid fileId, Exception exception);
    }
}

public sealed record IntegrationFileResponse(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    string? ChecksumSha256,
    string Status,
    FileRetentionResponse Retention,
    string ScanStatus,
    long SuccessfulDownloads,
    DateTimeOffset? LastDownloadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? DeletedAt,
    int Version,
    string Etag);

public sealed record IntegrationFileListResponse(
    IReadOnlyList<IntegrationFileResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record AvailabilityResponse(
    Guid FileId,
    string State,
    long SizeBytes,
    DateTimeOffset? ExpiresAt,
    string ScanStatus,
    DateTimeOffset CheckedAt);

public static class IntegrationFileMapper
{
    public static IntegrationFileResponse ToResponse(FileEntity file) => new(
        file.Id,
        file.OriginalFilename,
        file.SizeBytes,
        file.ContentType,
        file.ChecksumSha256,
        StatusName(file.Status),
        new FileRetentionResponse(RetentionModeName(file.RetentionMode), file.RetentionDays, file.ExpiresAt, file.RetentionReason),
        ScanStatusName(file.ScanStatus),
        file.SuccessfulDownloads,
        file.LastDownloadAt,
        file.CreatedAt,
        file.UpdatedAt,
        file.AvailableAt,
        file.DeletedAt,
        file.Version,
        $"\"{file.Version}\"");

    private static string StatusName(FileStatus status) => status switch
    {
        FileStatus.PendingUpload => "pending_upload",
        FileStatus.Processing => "processing",
        FileStatus.Available => "available",
        FileStatus.Quarantined => "quarantined",
        FileStatus.Expired => "expired",
        FileStatus.PendingDeletion => "pending_deletion",
        FileStatus.Deleted => "deleted",
        FileStatus.UploadFailed => "upload_failed",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string RetentionModeName(RetentionMode mode) => mode switch
    {
        RetentionMode.Finite => "finite",
        RetentionMode.Indefinite => "indefinite",
        _ => mode.ToString().ToLowerInvariant(),
    };

    private static string ScanStatusName(ScanStatus status) => status switch
    {
        ScanStatus.NotScanned => "not_scanned",
        ScanStatus.Pending => "pending",
        ScanStatus.Clean => "clean",
        ScanStatus.Infected => "infected",
        ScanStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant(),
    };
}
