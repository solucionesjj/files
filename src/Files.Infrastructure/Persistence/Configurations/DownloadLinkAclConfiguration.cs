using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class DownloadLinkAclConfiguration : IEntityTypeConfiguration<DownloadLinkAcl>
{
    public void Configure(EntityTypeBuilder<DownloadLinkAcl> builder)
    {
        builder.ToTable("download_link_acl");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.LinkId).IsRequired();
        builder.Property(x => x.PrincipalType).HasConversion(EnumConversion.SnakeCase<PrincipalType>());
        builder.Property(x => x.PrincipalId).IsRequired();

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.LinkId, x.PrincipalType, x.PrincipalId }).IsUnique();
    }
}
