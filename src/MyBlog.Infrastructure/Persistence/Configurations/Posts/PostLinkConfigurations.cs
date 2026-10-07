using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Infrastructure.Persistence.Configurations.Posts;

internal sealed class PostTagConfiguration : IEntityTypeConfiguration<PostTag>
{
    public void Configure(EntityTypeBuilder<PostTag> builder)
    {
        builder.ToTable("post_tags");

        builder.HasKey(t => new { t.PostId, t.TagId });

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(t => t.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TagId);
    }
}

internal sealed class PostMediaConfiguration : IEntityTypeConfiguration<PostMedia>
{
    public void Configure(EntityTypeBuilder<PostMedia> builder)
    {
        builder.ToTable("post_media");

        builder.HasKey(m => new { m.PostId, m.MediaFileId });

        builder.HasOne<MediaFile>()
            .WithMany()
            .HasForeignKey(m => m.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.MediaFileId);
    }
}
