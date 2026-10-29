using Files.Application.Configuration;
using Files.Infrastructure.DependencyInjection;
using Files.Worker;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    ConfigurationValidator.ThrowIfInvalid(builder.Configuration);

    builder.Services.AddFilesPersistence(builder.Configuration);
    builder.Services.AddFilesStorage(builder.Configuration);
    builder.Services.AddHostedService<UploadCleanupJob>();
    builder.Services.AddHostedService<UploadScanCommitJob>();
    builder.Services.AddHostedService<NotificationScheduleJob>();
    builder.Services.AddHostedService<NotificationSendJob>();
    builder.Services.AddHostedService<ExpirationJob>();
    builder.Services.AddHostedService<RetentionMigrationJob>();

    var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("files-worker"))
        .WithMetrics(metrics =>
        {
            metrics.AddRuntimeInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                var target = otlpEndpoint;
                metrics.AddOtlpExporter(options => ConfigureExporter(options, builder.Configuration, target));
            }
        });

    builder.Services.AddSerilog((_, logger) =>
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", "files-worker")
            .WriteTo.Console(new CompactJsonFormatter());

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            var target = otlpEndpoint;
            logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = target;
                options.Protocol = OtlpProtocolFromConfiguration(builder.Configuration);
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "files-worker",
                };
            });
        }
    });

    var host = builder.Build();
    host.Run();
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureExporter(OtlpExporterOptions options, IConfiguration configuration, string endpoint)
{
    options.Endpoint = new Uri(endpoint);
    options.Protocol = string.Equals(
        configuration["OTEL_EXPORTER_OTLP_PROTOCOL"],
        "grpc",
        StringComparison.OrdinalIgnoreCase)
        ? OtlpExportProtocol.Grpc
        : OtlpExportProtocol.HttpProtobuf;
}

static OtlpProtocol OtlpProtocolFromConfiguration(IConfiguration configuration)
{
    var value = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];
    return string.Equals(value, "grpc", StringComparison.OrdinalIgnoreCase)
        ? OtlpProtocol.Grpc
        : OtlpProtocol.HttpProtobuf;
}
