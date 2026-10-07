using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Comments;

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments", t => t.HasCheckConstraint(
            "ck_comments_depth",
            $"\"depth\" >= 0 AND \"depth\" < {CommentConstraints.MaxDepth}"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Content).HasMaxLength(CommentConstraints.ContentMaxLength).IsRequired();

        builder.Property<uint>(VersionProperty).IsRowVersion();

        builder.HasIndex(c => new { c.PostId, c.CreatedAt });
        builder.HasIndex(c => c.ParentId);
        builder.HasIndex(c => c.AuthorId);

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Comment>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
