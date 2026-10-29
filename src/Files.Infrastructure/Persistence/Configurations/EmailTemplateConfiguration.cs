using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Files.Infrastructure.Persistence.Configurations;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Key).IsRequired();
        builder.Property(x => x.Locale).IsRequired();
        builder.Property(x => x.Subject).IsRequired();
        builder.Property(x => x.BodyHtml).IsRequired();
        builder.Property(x => x.BodyText).IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.IsActive);
        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => new { x.Key, x.Locale, x.Version }).IsUnique();
    }
}
