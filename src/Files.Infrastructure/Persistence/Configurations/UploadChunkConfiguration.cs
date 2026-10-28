using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class UploadChunkConfiguration : IEntityTypeConfiguration<UploadChunk>
{
    public void Configure(EntityTypeBuilder<UploadChunk> builder)
    {
        builder.ToTable("upload_chunks");

        builder.HasKey(x => new { x.UploadSessionId, x.ChunkNumber });

        builder.Property(x => x.UploadSessionId).IsRequired();
        builder.Property(x => x.ChunkNumber).IsRequired();
        builder.Property(x => x.OffsetBytes).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ChecksumSha256);

        builder.Property(x => x.ReceivedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
