using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> builder)
    {
        builder.ToTable("integrations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Slug).IsRequired();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.Description);
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<IntegrationStatus>());
        builder.Property(x => x.EntraClientId);
        builder.Property(x => x.EntraServicePrincipalId);
        builder.Property(x => x.AllowedIpCidrs).HasColumnType("cidr[]");
        builder.Property(x => x.DisabledAt);
        builder.Property(x => x.DisabledBy);
        builder.Property(x => x.DisabledReason);
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.EntraClientId).IsUnique();
    }
}
