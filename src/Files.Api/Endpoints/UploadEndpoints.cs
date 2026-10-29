using Files.Api.Authentication;
using Files.Api.Middleware;
using Files.Application.Abstractions;
using Files.Application.Identity;
using Files.Application.Upload;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

/// <summary>Contract-first upload endpoints for portal users and integrations (ADR-0003).</summary>
public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/portal/upload-sessions", CreatePortalAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapPut("/v1/portal/upload-sessions/{sessionId:guid}/chunks/{chunkNumber:int}", UploadPortalChunkAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapGet("/v1/portal/upload-sessions/{sessionId:guid}", GetPortalStatusAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapPost("/v1/portal/upload-sessions/{sessionId:guid}/complete", CompletePortalAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapDelete("/v1/portal/upload-sessions/{sessionId:guid}", AbortPortalAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);

        endpoints.MapPost("/v1/integrations/upload-sessions", CreateIntegrationAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationWritePolicy)
            .WithMetadata(new IdempotentAttribute());
        endpoints.MapPut("/v1/integrations/upload-sessions/{sessionId:guid}/chunks/{chunkNumber:int}", UploadIntegrationChunkAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationWritePolicy);
        endpoints.MapGet("/v1/integrations/upload-sessions/{sessionId:guid}", GetIntegrationStatusAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationPolicy);
        endpoints.MapPost("/v1/integrations/upload-sessions/{sessionId:guid}/complete", CompleteIntegrationAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationWritePolicy)
            .WithMetadata(new IdempotentAttribute());
        endpoints.MapDelete("/v1/integrations/upload-sessions/{sessionId:guid}", AbortIntegrationAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationWritePolicy);
    }

    // ------------------------------------------------------------------ portal

    private static async Task<IResult> CreatePortalAsync(
        CreatePortalUploadRequest request,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await uploads.CreatePortalAsync(
            new CreatePortalUpload(
                request.FileName,
                request.SizeBytes,
                request.ContentType,
                request.ChecksumSha256,
                ToRetentionInput(request.Retention)),
            actor.User.Id,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        return result.IsSuccess
            ? Results.Created($"/v1/portal/upload-sessions/{result.Value!.UploadSessionId}", ToSessionResponse(result.Value))
            : ToProblem(context, result.Error!);
    }

    private static Task<IResult> UploadPortalChunkAsync(
        Guid sessionId,
        int chunkNumber,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct) =>
        UploadChunkAsync(sessionId, chunkNumber, context, identityService, uploads, portal: true, ct);

    private static async Task<IResult> GetPortalStatusAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await uploads.GetStatusAsync(sessionId, actor.User.Id, null, ct);
        return result.IsSuccess
            ? Results.Ok(ToStatusResponse(result.Value!))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> CompletePortalAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var checksum = await ReadCompleteChecksumAsync(context, ct);
        var result = await uploads.CompleteAsync(sessionId, checksum, actor.User.Id, null, ct);
        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        var file = result.Value!.File;
        context.Response.Headers.ETag = $"\"{file.Version}\"";
        return Results.Ok(FileMetadataMapper.ToResponse(file, []));
    }

    private static async Task<IResult> AbortPortalAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await uploads.AbortAsync(sessionId, actor.User.Id, null, RequestContext.TraceId(context), RequestContext.IpHash(context), ct);
        return result.IsSuccess ? Results.NoContent() : ToProblem(context, result.Error!);
    }

    // ------------------------------------------------------------------ integration

    private static async Task<IResult> CreateIntegrationAsync(
        CreateIntegrationUploadRequest request,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.Type != ActorType.Integration || actor.Id is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid API key");
        }

        var result = await uploads.CreateIntegrationAsync(
            new CreateIntegrationUpload(request.FileName, request.SizeBytes, request.ContentType, request.ChecksumSha256),
            actor.Id.Value,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        return result.IsSuccess
            ? Results.Created($"/v1/integrations/upload-sessions/{result.Value!.UploadSessionId}", ToSessionResponse(result.Value))
            : ToProblem(context, result.Error!);
    }

    private static Task<IResult> UploadIntegrationChunkAsync(
        Guid sessionId,
        int chunkNumber,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct) =>
        UploadChunkAsync(sessionId, chunkNumber, context, identityService, uploads, portal: false, ct);

    private static async Task<IResult> GetIntegrationStatusAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.Type != ActorType.Integration || actor.Id is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid API key");
        }

        var result = await uploads.GetStatusAsync(sessionId, null, actor.Id.Value, ct);
        return result.IsSuccess
            ? Results.Ok(ToStatusResponse(result.Value!))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> CompleteIntegrationAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.Type != ActorType.Integration || actor.Id is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid API key");
        }

        var checksum = await ReadCompleteChecksumAsync(context, ct);
        var result = await uploads.CompleteAsync(sessionId, checksum, null, actor.Id.Value, ct);
        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        context.Response.Headers.ETag = $"\"{result.Value!.File.Version}\"";
        return Results.Ok(FileMetadataMapper.ToResponse(result.Value!.File, []));
    }

    private static async Task<IResult> AbortIntegrationAsync(
        Guid sessionId,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (actor.Type != ActorType.Integration || actor.Id is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid API key");
        }

        var result = await uploads.AbortAsync(sessionId, null, actor.Id.Value, RequestContext.TraceId(context), RequestContext.IpHash(context), ct);
        return result.IsSuccess ? Results.NoContent() : ToProblem(context, result.Error!);
    }

    // ------------------------------------------------------------------ shared

    private static async Task<IResult> UploadChunkAsync(
        Guid sessionId,
        int chunkNumber,
        HttpContext context,
        IIdentityService identityService,
        IUploadService uploads,
        bool portal,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (portal ? actor.User is null : (actor.Type != ActorType.Integration || actor.Id is null))
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid credential");
        }

        if (context.Request.ContentLength is not long contentLength)
        {
            return Problems.Response(context, StatusCodes.Status411LengthRequired, "length_required", "Content-Length is required");
        }

        var checksum = context.Request.Headers["X-Chunk-SHA256"].FirstOrDefault();

        var result = await uploads.WriteChunkAsync(
            sessionId,
            chunkNumber,
            context.Request.Body,
            contentLength,
            checksum,
            portal ? actor.User!.Id : null,
            portal ? null : actor.Id!.Value,
            RequestContext.TraceId(context),
            RequestContext.IpHash(context),
            ct);

        return result.IsSuccess
            ? Results.Ok(ToChunkResponse(result.Value!))
            : ToProblem(context, result.Error!);
    }

    private static async Task<string?> ReadCompleteChecksumAsync(HttpContext context, CancellationToken ct)
    {
        if (context.Request.ContentLength is > 0)
        {
            var request = await context.Request.ReadFromJsonAsync<CompleteUploadRequest>(ct);
            return request?.ChecksumSha256;
        }

        return null;
    }

    private static IResult ToProblem(HttpContext context, ServiceError error) =>
        Problems.Response(context, error.Status, error.Code, error.Title, error.Detail);

    private static UploadSessionResponse ToSessionResponse(UploadSessionCreated created) => new(
        created.FileId, created.UploadSessionId, created.ChunkSizeBytes, created.ChunkCount, created.ExpiresAt, created.ReceivedBytes);

    private static UploadChunkResultResponse ToChunkResponse(ChunkAccepted accepted) => new(
        accepted.UploadSessionId, accepted.ChunkNumber, accepted.ReceivedBytes, accepted.ChunksReceived, accepted.ChunkCount, StatusName(accepted.Status));

    private static UploadSessionStatusResponse ToStatusResponse(UploadStatusView view) => new(
        view.FileId, view.UploadSessionId, StatusName(view.Status), view.ChunkSizeBytes, view.ChunkCount,
        view.ReceivedBytes, view.ChunksReceived, view.ExpiresAt, view.LastError);

    private static RetentionInput? ToRetentionInput(RetentionRequest? retention) =>
        retention is null ? null : new RetentionInput(retention.Mode, retention.Days, retention.Reason);

    private static string StatusName(UploadSessionStatus status) => status switch
    {
        UploadSessionStatus.Initiated => "initiated",
        UploadSessionStatus.Uploading => "uploading",
        UploadSessionStatus.Received => "received",
        UploadSessionStatus.Committing => "committing",
        UploadSessionStatus.Completed => "completed",
        UploadSessionStatus.Failed => "failed",
        UploadSessionStatus.Expired => "expired",
        UploadSessionStatus.Aborted => "aborted",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public sealed record CreatePortalUploadRequest(
    string FileName,
    long SizeBytes,
    string ContentType,
    string? ChecksumSha256,
    RetentionRequest? Retention,
    IReadOnlyList<string>? Tags);

public sealed record CreateIntegrationUploadRequest(
    string FileName,
    long SizeBytes,
    string ContentType,
    string? ChecksumSha256);

public sealed record RetentionRequest(string? Mode, int? Days, string? Reason);

public sealed record CompleteUploadRequest(string? ChecksumSha256);

public sealed record UploadSessionResponse(
    Guid FileId,
    Guid UploadSessionId,
    long ChunkSizeBytes,
    int ChunkCount,
    DateTimeOffset ExpiresAt,
    long ReceivedBytes);

public sealed record UploadChunkResultResponse(
    Guid UploadSessionId,
    int ChunkNumber,
    long ReceivedBytes,
    int ChunksReceived,
    int ChunkCount,
    string Status);

public sealed record UploadSessionStatusResponse(
    Guid FileId,
    Guid UploadSessionId,
    string Status,
    long ChunkSizeBytes,
    int ChunkCount,
    long ReceivedBytes,
    IReadOnlyList<int> ChunksReceived,
    DateTimeOffset ExpiresAt,
    string? LastError);
