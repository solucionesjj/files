using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Files.Application.Deletion;
using Files.Application.Files;
using Files.Application.Integrations;
using Files.Application.Notifications;
using Files.Application.Lifecycle;
using Files.Application.Storage;
using Files.Application.Telemetry;
using Files.Application.Upload;
using Files.Infrastructure.Email;
using Files.Infrastructure.Persistence.Repositories;
using Files.Infrastructure.Persistence.Writers;
using Files.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Infrastructure.DependencyInjection;

/// <summary>Registers the object store, staging store, upload repositories/writer, and the upload service.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddFilesStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(StorageOptions.FromConfiguration(configuration));
        services.AddSingleton(S3Options.FromConfiguration(configuration));
        services.AddSingleton(ClamAvOptions.FromConfiguration(configuration));
        services.AddSingleton(SmtpOptions.FromConfiguration(configuration));
        services.AddSingleton(SchedulerOptions.FromConfiguration(configuration));
        services.AddSingleton<LifecycleMetrics>();
        var linksOptions = LinksOptions.FromConfiguration(configuration);
        services.AddSingleton(linksOptions);
        services.AddSingleton(KeyThrottle.FromOptions(linksOptions));

        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddSingleton<IChunkStore, FileChunkStore>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // Fail closed (D-35): ClamAV is only wired when scanning is enabled and a host is present;
        // otherwise the placeholder scanner refuses to upload (never commits an unscanned file).
        var malwareScanning = configuration.GetValue("Features:MalwareScanning", false);
        var clamAvHost = configuration["ClamAV:Host"];
        if (malwareScanning && !string.IsNullOrWhiteSpace(clamAvHost))
        {
            services.AddSingleton<IMalwareScanner, ClamAvScanner>();
        }
        else
        {
            services.AddSingleton<IMalwareScanner, NullMalwareScanner>();
        }

        services.AddScoped<IUploadSessionRepository, UploadSessionRepository>();
        services.AddScoped<IUploadChunkRepository, UploadChunkRepository>();
        services.AddScoped<IUploadChunkWriter, UploadChunkWriter>();
        services.AddScoped<IScanWorkClaimer, ScanWorkClaimer>();
        services.AddScoped<IUploadCommitter, UploadCommitter>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IUploadCleanupService, UploadCleanupService>();
        services.AddScoped<IScanCommitService, ScanCommitService>();

        // S7 portal management (rename/tag/retention/delete + owner download grants).
        services.AddScoped<IFileManagementService, FileManagementService>();
        services.AddScoped<IDownloadGrantService, DownloadGrantService>();

        // S8 public links and downloads (ADR-0005).
        services.AddScoped<IPublicLinkService, PublicLinkService>();

        // S9 integration API (FR-INT): file access + idempotency + quotas.
        services.AddScoped<IIntegrationFileService, IntegrationFileService>();
        services.AddScoped<IIntegrationQuotaService, IntegrationQuotaService>();

        // S10 retention, legal hold, notifications (M6).
        services.AddScoped<INotificationScheduler, NotificationScheduler>();
        services.AddScoped<INotificationSender, NotificationSender>();
        services.AddScoped<IExpirationService, ExpirationService>();
        services.AddScoped<IRetentionMigrationService, RetentionMigrationService>();

        // S11 expiration & deletion completion (M6).
        services.AddScoped<IDeletionService, DeletionService>();
        services.AddScoped<ITombstonePurgeService, TombstonePurgeService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
