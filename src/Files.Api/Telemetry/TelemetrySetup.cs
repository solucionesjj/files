using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Files.Api.Telemetry;

/// <summary>
/// Wires OpenTelemetry traces (ASP.NET Core + HttpClient) and metrics (ASP.NET Core + HttpClient +
/// runtime) to the local collector. Exporters are only attached when an endpoint is configured so
/// a missing collector degrades gracefully instead of failing startup.
/// </summary>
public static class TelemetrySetup
{
    public static IServiceCollection AddFilesTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
                AddOtlpExporterIfConfigured(tracing, configuration);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
                AddOtlpExporterIfConfigured(metrics, configuration);
            });

        return services;
    }

    private static void AddOtlpExporterIfConfigured(TracerProviderBuilder tracing, IConfiguration configuration)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var target = endpoint;
            tracing.AddOtlpExporter(options => ConfigureExporter(options, configuration, target));
        }
    }

    private static void AddOtlpExporterIfConfigured(MeterProviderBuilder metrics, IConfiguration configuration)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var target = endpoint;
            metrics.AddOtlpExporter(options => ConfigureExporter(options, configuration, target));
        }
    }

    private static void ConfigureExporter(OtlpExporterOptions options, IConfiguration configuration, string endpoint)
    {
        options.Endpoint = new Uri(endpoint);
        options.Protocol = string.Equals(
            configuration["OTEL_EXPORTER_OTLP_PROTOCOL"],
            "grpc",
            StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;
    }
}
