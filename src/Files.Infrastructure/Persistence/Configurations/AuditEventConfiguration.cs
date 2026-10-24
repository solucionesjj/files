using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("file_audit_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId);
        builder.Property(x => x.ActorType).HasConversion(EnumConversion.SnakeCase<ActorType>());
        builder.Property(x => x.ActorId);
        builder.Property(x => x.Action).IsRequired();
        builder.Property(x => x.Result).HasConversion(EnumConversion.SnakeCase<AuditResult>());
        builder.Property(x => x.Reason);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");
        builder.Property(x => x.IpHash);
        builder.Property(x => x.TraceId);
        builder.Property(x => x.OccurredAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
