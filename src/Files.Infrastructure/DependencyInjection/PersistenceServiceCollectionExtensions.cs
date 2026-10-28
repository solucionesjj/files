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
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IUserProvisioner, UserProvisioner>();

        return services;
    }
}
