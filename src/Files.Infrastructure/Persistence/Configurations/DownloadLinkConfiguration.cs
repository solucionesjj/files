using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class DownloadLinkConfiguration : IEntityTypeConfiguration<DownloadLink>
{
    public void Configure(EntityTypeBuilder<DownloadLink> builder)
    {
        builder.ToTable("download_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.TokenHash).IsRequired();
        builder.Property(x => x.AccessMode).HasConversion(EnumConversion.SnakeCase<LinkAccessMode>());
        builder.Property(x => x.AllowAllTenant);
        builder.Property(x => x.DownloadKeyHash);
        builder.Property(x => x.DownloadKeyVersion);
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<LinkStatus>());
        builder.Property(x => x.ExpiresAt);
        builder.Property(x => x.MaxDownloads);
        builder.Property(x => x.SuccessfulDownloads);
        builder.Property(x => x.ReservedDownloads);
        builder.Property(x => x.CreatedByType).HasConversion(EnumConversion.SnakeCase<LinkCreatorType>());
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.RevokedAt);
        builder.Property(x => x.RevokedBy);
        builder.Property(x => x.RevokedReason);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => x.TokenHash).IsUnique();
    }
}
