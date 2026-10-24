using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("notification_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.PolicyId).IsRequired();
        builder.Property(x => x.IntervalDays).IsRequired();
        builder.Property(x => x.ExpirationAt).IsRequired();
        builder.Property(x => x.RecipientEmail).HasColumnType("citext");
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<NotificationStatus>());
        builder.Property(x => x.Attempts);
        builder.Property(x => x.NextAttemptAt);
        builder.Property(x => x.ProviderMessageId);
        builder.Property(x => x.LastError);
        builder.Property(x => x.ScheduledAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.SentAt);
        builder.Property(x => x.DeliveredAt);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => new { x.FileId, x.IntervalDays, x.ExpirationAt }).IsUnique();
    }
}
