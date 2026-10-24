using Files.Application.Abstractions;
using Files.Application.Identity;

namespace Files.Api.Identity;

public static class IdentitySetup
{
    public static IServiceCollection AddFilesIdentity(this IServiceCollection services)
    {
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<TokenCurrentActor>();
        services.AddScoped<ICurrentActor>(sp => sp.GetRequiredService<TokenCurrentActor>());
        return services;
    }
}
