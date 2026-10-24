namespace Files.Api.Identity;

/// <summary>
/// Resolves the current actor for authenticated requests (JIT user provisioning or integration
/// lookup) before endpoints run, so <see cref="ICurrentActor"/> is populated for every handler.
/// </summary>
public sealed class CurrentActorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var actor = context.RequestServices.GetRequiredService<TokenCurrentActor>();
            await actor.InitializeAsync(context.RequestAborted);
        }

        await next(context);
    }
}
