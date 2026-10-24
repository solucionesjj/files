using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class IntegrationPermissionConfiguration : IEntityTypeConfiguration<IntegrationPermissionEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationPermissionEntry> builder)
    {
        builder.ToTable("integration_permissions");

        builder.HasKey(x => new { x.IntegrationId, x.Permission });

        builder.Property(x => x.IntegrationId).IsRequired();
        builder.Property(x => x.Permission).IsRequired().HasMaxLength(32);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
