using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("retention_policies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DefaultDays).IsRequired();
        builder.Property(x => x.MaxDays).IsRequired();
        builder.Property(x => x.AllowUserOverride);
        builder.Property(x => x.AllowIndefinite);
        builder.Property(x => x.PublicLinksEnabled);
        builder.Property(x => x.IsActive);
        builder.Property(x => x.EffectiveFrom).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.Notes);
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
