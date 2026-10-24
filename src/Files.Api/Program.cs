using Files.Api;
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
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseSerilogRequestLogging(options =>
    {
        // Route templates are logged, never the raw path (which may carry link/grant tokens).
        options.MessageTemplate = "HTTP {RequestMethod} {Route} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            diagnosticContext.Set("Route", httpContext.GetEndpoint()?.DisplayName ?? "<unmatched>");
    });

    app.MapHealthEndpoints();
    app.MapConfigEndpoint(builder.Configuration, app.Environment);

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
