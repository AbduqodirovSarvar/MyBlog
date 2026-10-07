using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;
using NpgsqlTypes;
using static MyBlog.Domain.Posts.PostConstraints;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Posts;

internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public const string SearchVectorProperty = "SearchVector";

    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("posts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // title, summary va content_plain_text nomlari search_vector SQL'ida ishlatiladi — aniq ko'rsatamiz
        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(TitleMaxLength).IsRequired();
        builder.Property(p => p.Summary).HasColumnName("summary").HasMaxLength(SummaryMaxLength);
        builder.Property(p => p.Slug).HasMaxLength(SlugMaxLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();

        builder.OwnsOne(p => p.Content, content => PostContentMapping.Configure(content));
        builder.Navigation(p => p.Content).IsRequired();

        builder.OwnsOne(p => p.Seo, seo =>
        {
            seo.Property(s => s.MetaTitle).HasColumnName("seo_meta_title").HasMaxLength(MetaTitleMaxLength);
            seo.Property(s => s.MetaDescription).HasColumnName("seo_meta_description").HasMaxLength(MetaDescriptionMaxLength);
            seo.Property(s => s.OgImageMediaId).HasColumnName("seo_og_image_media_id");
            seo.Property(s => s.CanonicalUrl).HasColumnName("seo_canonical_url").HasMaxLength(CanonicalUrlMaxLength);
        });
        builder.Navigation(p => p.Seo).IsRequired();

        // To'liq matnli qidiruv: saqlanadigan generated ustun + GIN index
        builder.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content_plain_text, ''))",
                stored: true);
        builder.HasIndex(SearchVectorProperty).HasMethod("GIN");

        // Optimistic concurrency: Npgsql uint + IsRowVersion'ni PostgreSQL xmin tizim ustuniga map qiladi
        builder.Property<uint>(VersionProperty).IsRowVersion();

        builder.HasIndex(p => new { p.OwnerId, p.Slug }).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(p => new { p.Status, p.PublishedAt }).IsDescending(false, true);
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Tags)
            .WithOne()
            .HasForeignKey(t => t.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Media)
            .WithOne()
            .HasForeignKey(m => m.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Kichik join jadvallar aggregate'ning bir qismi — SetTags/UpdateContent to'g'ri ishlashi uchun avtomatik yuklanadi
        builder.Navigation(p => p.Tags).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(p => p.Media).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary>PostContent ustunlari Post va PostRevision'da bir xil nomlanadi.</summary>
internal static class PostContentMapping
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, PostContent> content) where TOwner : class
    {
        content.Property(c => c.Format).HasColumnName("content_format").HasMaxLength(ContentFormatMaxLength).IsRequired();
        content.Property(c => c.Html).HasColumnName("content_html").IsRequired();
        // Raw — muharrirning ixtiyoriy formatdagi hujjati, jsonb emas (har doim ham JSON bo'lmaydi)
        content.Property(c => c.Raw).HasColumnName("content_raw");
        content.Property(c => c.PlainText).HasColumnName("content_plain_text").IsRequired();
        content.Property(c => c.TableOfContentsJson).HasColumnName("content_toc");
    }
}
