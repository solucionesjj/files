using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class NotificationPolicyConfiguration : IEntityTypeConfiguration<NotificationPolicy>
{
    public void Configure(EntityTypeBuilder<NotificationPolicy> builder)
    {
        builder.ToTable("notification_policies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.IntervalsDays).HasColumnType("integer[]").IsRequired();
        builder.Property(x => x.TemplateKey).IsRequired();
        builder.Property(x => x.IsActive);
        builder.Property(x => x.EffectiveFrom).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
