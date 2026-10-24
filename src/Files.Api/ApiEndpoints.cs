using System.Reflection;
using Files.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Files.Api;

/// <summary>Minimal-API endpoints for liveness/readiness and public configuration.</summary>
public static class ApiEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is up. No dependency checks are run.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse,
        });

        // Readiness: real dependency checks (PostgreSQL) must pass.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse,
        });
    }

    public static void MapConfigEndpoint(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        endpoints.MapGet("/v1/config", () =>
            Results.Ok(BuildPublicConfig(configuration, environment)));
    }

    private static PublicConfig BuildPublicConfig(
        IConfiguration configuration,
        IWebHostEnvironment environment) => new(
            Service: "files-api",
            Version: GetVersion(),
            Environment: environment.EnvironmentName,
            Features: new FeatureFlags(
                PublicLinks: configuration.GetValue("Features:PublicLinks", true),
                MalwareScanning: configuration.GetValue("Features:MalwareScanning", false),
                Notifications: configuration.GetValue("Features:Notifications", true)),
            Portal: new PortalInfo(
                BaseUrl: configuration["Portal:BaseUrl"] ?? "http://localhost:4200",
                SupportEmail: configuration["Portal:SupportEmail"] ?? "files-support@example.com"),
            I18n: new I18nInfo(
                DefaultLocale: "es-CO",
                SupportedLocales: ["es-CO", "en-US"]),
            Retention: new RetentionInfo(
                DefaultDays: configuration.GetValue("Retention:DefaultDays", 30),
                MaxDays: configuration.GetValue("Retention:MaxDays", 365)),
            Links: new LinksInfo(
                PublicTtlDays: configuration.GetValue("Links:PublicTtlDays", 15),
                PrivateTtlDays: configuration.GetValue("Links:PrivateTtlDays", 30)));

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                }),
        };

        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }
}
