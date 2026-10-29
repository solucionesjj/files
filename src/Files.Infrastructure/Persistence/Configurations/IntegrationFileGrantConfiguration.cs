using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping of <c>integration_file_grants</c> (read-only cross-integration access).</summary>
public sealed class IntegrationFileGrantConfiguration : IEntityTypeConfiguration<IntegrationFileGrant>
{
    public void Configure(EntityTypeBuilder<IntegrationFileGrant> builder)
    {
        builder.ToTable("integration_file_grants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.IntegrationId).IsRequired();
        builder.Property(x => x.GrantedBy);
        builder.Property(x => x.ExpiresAt);
        builder.Property(x => x.RevokedAt);
        builder.Property(x => x.RevokedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.FileId, x.IntegrationId }).IsUnique()
            .HasFilter("revoked_at IS NULL");
    }
}
