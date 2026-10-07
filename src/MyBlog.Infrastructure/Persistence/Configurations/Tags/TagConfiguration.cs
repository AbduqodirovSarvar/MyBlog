using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Tags;
using static MyBlog.Domain.Tags.TagConstraints;

namespace MyBlog.Infrastructure.Persistence.Configurations.Tags;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name).HasMaxLength(NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(SlugMaxLength).IsRequired();

        builder.HasIndex(t => new { t.OwnerId, t.Slug }).IsUnique();
    }
}
