using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Media;
using static MyBlog.Domain.Media.MediaConstraints;

namespace MyBlog.Infrastructure.Persistence.Configurations.Media;

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.OriginalFileName).HasMaxLength(OriginalFileNameMaxLength).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(ContentTypeMaxLength).IsRequired();
        builder.Property(m => m.StorageKey).HasMaxLength(StorageKeyMaxLength).IsRequired();
        builder.Property(m => m.AltText).HasMaxLength(AltTextMaxLength);
        builder.Property(m => m.Caption).HasMaxLength(CaptionMaxLength);

        builder.Ignore(m => m.IsImage);

        builder.HasIndex(m => new { m.OwnerId, m.CreatedAt });
        builder.HasIndex(m => m.StorageKey).IsUnique();

        // Variantlar bitta jsonb ustunda (alohida jadval shart emas)
        builder.OwnsMany(m => m.Variants, variants => variants.ToJson("variants"));
        builder.Navigation(m => m.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
