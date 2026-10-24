using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ObjectId).IsRequired();
        builder.Property(x => x.Email).HasColumnType("citext");
        builder.Property(x => x.DisplayName).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<UserStatus>());
        builder.Property(x => x.LastLoginAt);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.ObjectId }).IsUnique();
    }
}
