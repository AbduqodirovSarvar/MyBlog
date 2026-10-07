using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Categories;
using static MyBlog.Domain.Categories.CategoryConstraints;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Categories;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Slug).HasMaxLength(SlugMaxLength).IsRequired();

        builder.HasIndex(c => new { c.OwnerId, c.Slug }).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(c => new { c.OwnerId, c.ParentId, c.Order });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Translations)
            .WithOne()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tarjimalar aggregate'ning bir qismi va har doim kerak — avtomatik yuklanadi
        builder.Navigation(c => c.Translations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}

internal sealed class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder)
    {
        builder.ToTable("category_translations");

        builder.HasKey(t => new { t.CategoryId, t.Culture });

        builder.Property(t => t.Culture).HasMaxLength(CultureMaxLength).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(NameMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(DescriptionMaxLength);
    }
}
