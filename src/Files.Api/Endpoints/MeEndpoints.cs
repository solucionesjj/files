using Files.Api.Authentication;
using Files.Application.Identity;

namespace Files.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/me", GetMeAsync)
            .RequireAuthorization(AuthenticationSetup.PortalPolicy);
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context,
        IIdentityService identityService,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var user = actor.User;
        return Results.Ok(new MeResponse(
            user.Id,
            user.TenantId,
            user.Email,
            user.DisplayName,
            actor.Roles,
            Permissions: []));
    }
}
