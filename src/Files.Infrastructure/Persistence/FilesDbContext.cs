using Files.Domain.Common;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping of the canonical Flyway schema. This context maps the schema but does not own
/// it: migrations are SQL-only (ADR-0002) and the model is validated against a real database by
/// integration tests. IDs are application-generated UUIDv7.
/// </summary>
public sealed class FilesDbContext : DbContext
{
    public FilesDbContext(DbContextOptions<FilesDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Integration> Integrations => Set<Integration>();

    public DbSet<IntegrationApiKey> IntegrationApiKeys => Set<IntegrationApiKey>();

    public DbSet<IntegrationPermissionEntry> IntegrationPermissions => Set<IntegrationPermissionEntry>();

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();

    public DbSet<UploadChunk> UploadChunks => Set<UploadChunk>();

    public DbSet<DownloadLink> DownloadLinks => Set<DownloadLink>();

    public DbSet<DownloadLinkAcl> DownloadLinkAcls => Set<DownloadLinkAcl>();

    public DbSet<DownloadGrant> DownloadGrants => Set<DownloadGrant>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<FileTag> FileTags => Set<FileTag>();

    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<NotificationPolicy> NotificationPolicies => Set<NotificationPolicy>();

    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FilesDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Optimistic concurrency: bump the integer `version` token before every update. EF emits
        // `SET version = new WHERE version = original`, so a stale write conflicts.
        foreach (var entry in ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Modified && e.Entity is IVersionedEntity))
        {
            ((IVersionedEntity)entry.Entity).Version++;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
