using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class IntegrationApiKeyConfiguration : IEntityTypeConfiguration<IntegrationApiKey>
{
    public void Configure(EntityTypeBuilder<IntegrationApiKey> builder)
    {
        builder.ToTable("integration_api_keys");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IntegrationId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Prefix).IsRequired().HasMaxLength(32);
        builder.Property(x => x.SecretHash).IsRequired();
        builder.Property(x => x.Scopes).HasColumnType("text[]");
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.LastUsedAt);
        builder.Property(x => x.ExpiresAt);
        builder.Property(x => x.RevokedAt);
        builder.Property(x => x.RevokedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => x.IntegrationId);
    }
}
