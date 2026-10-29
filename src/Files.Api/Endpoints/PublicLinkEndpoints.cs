using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Files;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

/// <summary>
/// Public links and downloads (S8 / ADR-0005): owner-scoped create/list/revoke/stats plus the
/// anonymous metadata and key-redemption endpoints. Tokens and keys are never returned from the
/// list/read endpoints; the key is returned exactly once at creation.
/// </summary>
public static class PublicLinkEndpoints
{
    public static void MapPublicLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/portal/files/{fileId:guid}/links", ListFileLinksAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapPost("/v1/portal/files/{fileId:guid}/links", CreateLinkAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapGet("/v1/portal/links", ListMyLinksAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapDelete("/v1/portal/links/{linkId:guid}", RevokeLinkAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);
        endpoints.MapGet("/v1/portal/links/{linkId:guid}/stats", GetLinkStatsAsync)
            .RequireAuthorization(AuthenticationSetup.UserPolicy);

        endpoints.MapGet("/v1/public/download-links/{token}", GetPublicLinkAsync);
        endpoints.MapPost("/v1/public/download-links/{token}/downloads", RedeemPublicLinkAsync);
    }

    private static async Task<IResult> ListFileLinksAsync(
        Guid fileId,
        HttpContext context,
        IIdentityService identityService,
        IPublicLinkService links,
        TimeProvider timeProvider,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        if (await ResolveUserAsync(identityService, ct) is not { } user)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!TryResolvePagination(cursor, limit, context, out var decoded, out var pageLimit))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid pagination parameters");
        }

        var result = await links.ListFileLinksAsync(fileId, user.Id, decoded, pageLimit, ct);
        return result.IsSuccess
            ? Results.Ok(ToLinkList(result.Value!, timeProvider.GetUtcNow()))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> CreateLinkAsync(
        Guid fileId,
        CreateLinkRequest request,
        HttpContext context,
        IIdentityService identityService,
        IPublicLinkService links,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (await ResolveUserAsync(identityService, ct) is not { } user)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!string.Equals(request.AccessMode, "public", StringComparison.Ordinal))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "accessMode must be 'public'");
        }

        if (request.MaxDownloads is { } max && max is < 1 or > 1_000_000)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "maxDownloads must be between 1 and 1000000");
        }

        var result = await links.CreateAsync(
            fileId, user.Id, request.MaxDownloads, RequestContext.TraceId(context), RequestContext.IpHash(context), ct);

        if (!result.IsSuccess)
        {
            return ToProblem(context, result.Error!);
        }

        return Results.Created($"/v1/portal/links/{result.Value!.Link.Id}", LinkMapper.ToCreatedResponse(result.Value, timeProvider.GetUtcNow()));
    }

    private static async Task<IResult> ListMyLinksAsync(
        HttpContext context,
        IIdentityService identityService,
        IPublicLinkService links,
        TimeProvider timeProvider,
        string? status,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        if (await ResolveUserAsync(identityService, ct) is not { } user)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!LinkQueryParsing.TryParseStatus(status, out var parsedStatus))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid status");
        }

        if (!TryResolvePagination(cursor, limit, context, out var decoded, out var pageLimit))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid pagination parameters");
        }

        var result = await links.ListMyLinksAsync(user.Id, parsedStatus, decoded, pageLimit, ct);
        return result.IsSuccess
            ? Results.Ok(ToLinkList(result.Value!, timeProvider.GetUtcNow()))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> RevokeLinkAsync(
        Guid linkId,
        HttpContext context,
        IIdentityService identityService,
        IPublicLinkService links,
        string? reason,
        CancellationToken ct)
    {
        if (await ResolveUserAsync(identityService, ct) is not { } user)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await links.RevokeAsync(
            linkId, user.Id, reason, RequestContext.TraceId(context), RequestContext.IpHash(context), ct);

        return result.IsSuccess ? Results.NoContent() : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> GetLinkStatsAsync(
        Guid linkId,
        HttpContext context,
        IIdentityService identityService,
        IPublicLinkService links,
        CancellationToken ct)
    {
        if (await ResolveUserAsync(identityService, ct) is not { } user)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var result = await links.GetStatsAsync(linkId, user.Id, ct);
        return result.IsSuccess
            ? Results.Ok(LinkMapper.ToStatsResponse(result.Value!))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> GetPublicLinkAsync(
        string token,
        HttpContext context,
        IPublicLinkService links,
        CancellationToken ct)
    {
        var result = await links.GetPublicAsync(token, ct);
        return result.IsSuccess
            ? Results.Ok(LinkMapper.ToPublicInfoResponse(result.Value!))
            : ToProblem(context, result.Error!);
    }

    private static async Task<IResult> RedeemPublicLinkAsync(
        string token,
        RedeemPublicLinkRequest request,
        HttpContext context,
        IPublicLinkService links,
        CancellationToken ct)
    {
        if (!DownloadKeys.IsValidShape(request.Key))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "key must be exactly 8 alphanumeric characters");
        }

        var result = await links.RedeemAsync(token, request.Key, RequestContext.IpHash(context), ct);
        if (!result.IsSuccess)
        {
            var problem = ToProblem(context, result.Error!);
            if (result.Error!.Status == StatusCodes.Status429TooManyRequests)
            {
                context.Response.Headers.RetryAfter = "900";
            }

            return problem;
        }

        return Results.Created($"/v1/download-grants/{result.Value!.GrantToken}", result.Value);
    }

    private static async Task<User?> ResolveUserAsync(
        IIdentityService identityService,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        return actor.User ?? null;
    }

    private static bool TryResolvePagination(
        string? cursor,
        int? limit,
        HttpContext context,
        out LinkCursor? decoded,
        out int pageLimit)
    {
        if (!LinkQueryParsing.TryDecodeCursor(cursor, out decoded))
        {
            pageLimit = 0;
            return false;
        }

        pageLimit = limit ?? 25;
        return pageLimit is >= 1 and <= 100;
    }

    private static LinkListResponse ToLinkList(LinkListResult result, DateTimeOffset now)
    {
        return new LinkListResponse(
            result.Items.Select(l => LinkMapper.ToResponse(l, now)).ToArray(),
            result.HasMore && result.NextCursor is { } next ? LinkQueryParsing.EncodeCursor(next) : null,
            result.HasMore);
    }

    private static IResult ToProblem(HttpContext context, ServiceError error) =>
        Problems.Response(context, error.Status, error.Code, error.Title, error.Detail);
}
