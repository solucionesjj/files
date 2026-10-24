using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Scope).HasConversion(EnumConversion.SnakeCase<TagScope>());
        builder.Property(x => x.OwnerUserId);
        builder.Property(x => x.Name).HasColumnType("citext").IsRequired();
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.Scope, x.OwnerUserId, x.Name }).IsUnique();
    }
}
