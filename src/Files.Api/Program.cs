using Files.Api;
using Files.Api.Authentication;
using Files.Api.Endpoints;
using Files.Api.Identity;
using Files.Api.Logging;
using Files.Api.Middleware;
using Files.Api.Telemetry;
using Files.Application.Configuration;
using Files.Infrastructure.DependencyInjection;
using Files.Infrastructure.Health;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Fail fast on missing or malformed critical configuration. Only key names are reported.
    ConfigurationValidator.ThrowIfInvalid(builder.Configuration);

    builder.Host.UseSerilog((context, services, logger) =>
        LoggingSetup.Configure(
            logger,
            context.Configuration,
            "files-api",
            services.GetRequiredService<IHttpContextAccessor>()));

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    // Persistence: EF Core maps the Flyway-owned schema (no auto-migration on startup, ADR-0002).
    builder.Services.AddFilesPersistence(builder.Configuration);

    // Entra ID JWT validation (BearerUser + BearerIntegration) and authorization policies.
    builder.Services.AddFilesAuthentication(builder.Configuration);

    // Token-backed ICurrentActor and JIT identity resolution.
    builder.Services.AddFilesIdentity();

    // CORS: exact configured portal origins only (SPA is same-origin behind Caddy in production).
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "X-Request-Id", "Range", "X-Chunk-SHA256")
            .WithExposedHeaders("X-Request-Id", "RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset", "Retry-After", "ETag")));
    }

    builder.Services.AddFilesTelemetry(builder.Configuration, "files-api");

    // The API is only reachable through Caddy on the internal network; trust its forwarded headers.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    var app = builder.Build();

    app.UseForwardedHeaders();
    if (allowedOrigins.Length > 0)
    {
        app.UseCors(CorsPolicyName);
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseAuthentication();
    app.UseMiddleware<CurrentActorMiddleware>();
    app.UseAuthorization();

    app.UseSerilogRequestLogging(options =>
    {
        // Route templates are logged, never the raw path (which may carry link/grant tokens).
        options.MessageTemplate = "HTTP {RequestMethod} {Route} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            diagnosticContext.Set("Route", httpContext.GetEndpoint()?.DisplayName ?? "<unmatched>");
    });

    app.MapHealthEndpoints();
    app.MapConfigEndpoint(builder.Configuration, app.Environment);
    app.MapMeEndpoint();
    app.MapAdminIntegrationEndpoints();

    app.MapFallback((HttpContext context) =>
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not Found",
            Type = "https://www.rfc-editor.org/rfc/rfc9457.html",
        };
        problem.Extensions["code"] = "not_found";
        problem.Extensions["traceId"] = CorrelationIdMiddleware.GetCorrelationId(context);
        return Results.Json(problem, statusCode: StatusCodes.Status404NotFound);
    });

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    private const string CorsPolicyName = "files";
}
