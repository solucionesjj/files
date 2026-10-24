using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class DownloadGrantConfiguration : IEntityTypeConfiguration<DownloadGrant>
{
    public void Configure(EntityTypeBuilder<DownloadGrant> builder)
    {
        builder.ToTable("download_grants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.LinkId);
        builder.Property(x => x.GrantTokenHash).IsRequired();
        builder.Property(x => x.Source).HasConversion(EnumConversion.SnakeCase<GrantSource>());
        builder.Property(x => x.IssuedToUserId);
        builder.Property(x => x.IssuedIpHash);
        builder.Property(x => x.State).HasConversion(EnumConversion.SnakeCase<GrantState>());
        builder.Property(x => x.ReservedSlot);
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.RedeemedAt);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.BytesSent);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => x.GrantTokenHash).IsUnique();
    }
}
