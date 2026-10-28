using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class FileTagConfiguration : IEntityTypeConfiguration<FileTag>
{
    public void Configure(EntityTypeBuilder<FileTag> builder)
    {
        builder.ToTable("file_tags");

        builder.HasKey(x => new { x.FileId, x.TagId });
        builder.Property(x => x.FileId).ValueGeneratedNever();
        builder.Property(x => x.TagId).ValueGeneratedNever();
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId);
    }
}
