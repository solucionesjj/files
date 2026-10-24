using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Files.Api.Logging;

/// <summary>
/// Builds the Serilog pipeline: structured JSON to the console plus OTLP logs to the local
/// collector when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured.
/// </summary>
public static class LoggingSetup
{
    public static void Configure(
        LoggerConfiguration logger,
        IConfiguration configuration,
        string serviceName,
        IHttpContextAccessor? accessor = null)
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName);

        if (accessor is not null)
        {
            logger.Enrich.With(new RequestContextEnricher(accessor));
        }

        logger.WriteTo.Console(new CompactJsonFormatter());

        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.Protocol = OtlpProtocolFromConfiguration(configuration);
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName,
                };
            });
        }
    }

    private static OtlpProtocol OtlpProtocolFromConfiguration(IConfiguration configuration)
    {
        var value = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];
        return string.Equals(value, "grpc", StringComparison.OrdinalIgnoreCase)
            ? OtlpProtocol.Grpc
            : OtlpProtocol.HttpProtobuf;
    }
}
