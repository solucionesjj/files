using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class RetentionMigrationConfiguration : IEntityTypeConfiguration<RetentionMigration>
{
    public void Configure(EntityTypeBuilder<RetentionMigration> builder)
    {
        builder.ToTable("retention_migrations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<RetentionMigrationStatus>());
        builder.Property(x => x.TargetPolicyId).IsRequired();
        builder.Property(x => x.Scope).HasColumnType("jsonb");
        builder.Property(x => x.Reason).IsRequired();
        builder.Property(x => x.TotalFiles);
        builder.Property(x => x.ProcessedFiles);
        builder.Property(x => x.FailedFiles);
        builder.Property(x => x.LastError);
        builder.Property(x => x.RequestedBy).IsRequired();

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.StartedAt);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
    }
}
