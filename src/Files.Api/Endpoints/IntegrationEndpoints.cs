using Files.Api.Authentication;
using Files.Application.Identity;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

/// <summary>Minimal integration endpoint used to exercise API-key authentication end to end (ADR-0014).</summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/integrations/me", GetMeAsync)
            .RequireAuthorization(AuthenticationSetup.IntegrationPolicy);
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context,
        IIdentityService identityService,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.Type != ActorType.Integration || actor.Id is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "Invalid API key");
        }

        return Results.Ok(new IntegrationMeResponse(actor.Id.Value, actor.Permissions));
    }

    public sealed record IntegrationMeResponse(Guid IntegrationId, IReadOnlySet<string> Permissions);
}
