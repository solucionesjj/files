using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class FileEntityConfiguration : IEntityTypeConfiguration<FileEntity>
{
    public void Configure(EntityTypeBuilder<FileEntity> builder)
    {
        builder.ToTable("files");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerType).HasConversion(EnumConversion.SnakeCase<OwnerType>());
        builder.Property(x => x.OwnerUserId);
        builder.Property(x => x.OwnerIntegrationId);
        builder.Property(x => x.OriginalFilename).IsRequired();
        builder.Property(x => x.StorageProvider).HasConversion(EnumConversion.SnakeCase<StorageProvider>());
        builder.Property(x => x.StorageBucket).IsRequired();
        builder.Property(x => x.StorageKey);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ContentType).IsRequired();
        builder.Property(x => x.ContentTypeSource).HasConversion(EnumConversion.SnakeCase<ContentTypeSource>());
        builder.Property(x => x.ChecksumSha256);
        builder.Property(x => x.Status).HasConversion(EnumConversion.SnakeCase<FileStatus>());
        builder.Property(x => x.AccessMode).HasConversion(EnumConversion.SnakeCase<AccessMode>());
        builder.Property(x => x.RetentionMode).HasConversion(EnumConversion.SnakeCase<RetentionMode>());
        builder.Property(x => x.ExpiresAt);
        builder.Property(x => x.RetentionDays);
        builder.Property(x => x.RetentionPolicyId);
        builder.Property(x => x.ScanStatus).HasConversion(EnumConversion.SnakeCase<ScanStatus>());
        builder.Property(x => x.ScanCompletedAt);
        builder.Property(x => x.ScanAttempts);
        builder.Property(x => x.ScanNextAttemptAt);
        builder.Property(x => x.QuarantineReason);
        builder.Property(x => x.LegalHold);
        builder.Property(x => x.LegalHoldReason);
        builder.Property(x => x.LegalHoldSetAt);
        builder.Property(x => x.LegalHoldSetBy);
        builder.Property(x => x.LegalHoldReviewAt);
        builder.Property(x => x.RetentionReason);
        builder.Property(x => x.SuccessfulDownloads);
        builder.Property(x => x.LastDownloadAt);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");
        builder.Property(x => x.AvailableAt);
        builder.Property(x => x.DeletionRequestedByType)
            .HasConversion(EnumConversion.SnakeCase<DeletionRequesterType>());
        builder.Property(x => x.DeletionRequestedBy);
        builder.Property(x => x.DeletionReason);
        builder.Property(x => x.DeletionAttempts);
        builder.Property(x => x.NextDeletionAt);
        builder.Property(x => x.LastDeletionError);
        builder.Property(x => x.DeletedAt);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => x.StorageKey).IsUnique();
    }
}
