using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Infrastructure.Identity;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Repositories;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Infrastructure.DependencyInjection;

/// <summary>Registers EF Core persistence, repositories, the Dapper outbox/audit writers, and JIT provisioning.</summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFilesPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FilesDb")
            ?? throw new InvalidOperationException("ConnectionStrings__FilesDb is required.");

        // Schema is owned by Flyway; EF Core maps it and never migrates (ADR-0002).
        services.AddDbContext<FilesDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IUnitOfWork, FilesUnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IIntegrationApiKeyRepository, IntegrationApiKeyRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IIntegrationFileRepository, IntegrationFileRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IDailyUsageRepository, DailyUsageRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IDownloadLinkRepository, DownloadLinkRepository>();
        services.AddScoped<IDownloadGrantRepository, DownloadGrantRepository>();
        services.AddScoped<IKeyAttemptRepository, KeyAttemptRepository>();
        services.AddScoped<IPlatformSettingsRepository, PlatformSettingsRepository>();
        services.AddScoped<IRetentionPolicyRepository, RetentionPolicyRepository>();
        services.AddScoped<INotificationPolicyRepository, NotificationPolicyRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IRetentionMigrationRepository, RetentionMigrationRepository>();
        services.AddScoped<INotificationEventRepository, NotificationEventRepository>();
        services.AddScoped<IFileExpiryRepository, FileExpiryRepository>();
        services.AddScoped<IFileDeletionRepository, FileDeletionRepository>();
        services.AddScoped<IFileReconciliationRepository, FileReconciliationRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IUserProvisioner, UserProvisioner>();

        return services;
    }
}
