using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Posts;
using static MyBlog.Domain.Posts.PostConstraints;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Posts;

internal sealed class PostRevisionConfiguration : IEntityTypeConfiguration<PostRevision>
{
    public void Configure(EntityTypeBuilder<PostRevision> builder)
    {
        builder.ToTable("post_revisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Title).HasMaxLength(TitleMaxLength).IsRequired();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();

        builder.OwnsOne(r => r.Content, content => PostContentMapping.Configure(content));
        builder.Navigation(r => r.Content).IsRequired();

        builder.HasIndex(r => new { r.PostId, r.RevisionNumber });

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
