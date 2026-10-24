using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("upload_sessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.InitiatorType).HasConversion(EnumConversion.SnakeCase<UploadInitiatorType>());
        builder.Property(x => x.InitiatorId).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<UploadSessionStatus>());
        builder.Property(x => x.ExpectedSizeBytes).IsRequired();
        builder.Property(x => x.ExpectedContentType).IsRequired();
        builder.Property(x => x.ExpectedChecksumSha256);
        builder.Property(x => x.ChunkSizeBytes).IsRequired();
        builder.Property(x => x.ChunkCount).IsRequired();
        builder.Property(x => x.ReceivedBytes);
        builder.Property(x => x.StagingPath).IsRequired();
        builder.Property(x => x.S3UploadId).HasColumnName("s3_upload_id");
        builder.Property(x => x.S3PartSizeBytes).HasColumnName("s3_part_size_bytes");
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.LastError);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => x.StagingPath).IsUnique();
    }
}
