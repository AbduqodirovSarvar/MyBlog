using System.Text.Json;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Posts.Common;

internal sealed record ProcessedPostContent(PostContent Content, int ReadingTimeMinutes, IReadOnlyList<Guid> MediaIds);

/// <summary>Create/Update/Autosave/Restore uchun umumiy qadamlar.</summary>
internal static class PostEditing
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Kontentni format strategiyasi orqali qayta ishlaydi va domain PostContent'ga aylantiradi.</summary>
    public static async Task<Result<ProcessedPostContent>> ProcessContentAsync(IContentProcessorFactory factory,
        ContentInput input, Guid ownerId, CancellationToken cancellationToken)
    {
        var processor = factory.Get(input.Format);
        if (processor.IsFailure)
            return processor.Error;

        var processed = await processor.Value.ProcessAsync(input, ownerId, cancellationToken);
        if (processed.IsFailure)
            return processed.Error;

        var value = processed.Value;
        var toc = value.Toc.Count == 0 ? null : JsonSerializer.Serialize(value.Toc, JsonOptions);

        var content = PostContent.Create(value.Format, value.Html, value.Raw, value.PlainText, toc);
        if (content.IsFailure)
            return content.Error;

        return new ProcessedPostContent(content.Value, value.ReadingTimeMinutes, value.MediaIds);
    }

    /// <summary>Kategoriya — o'ziniki va faol; muqova va OG rasm — o'z media fayllari.</summary>
    public static async Task<Result> ValidateReferencesAsync(IReadRepository<Category> categories,
        IReadRepository<MediaFile> media, Guid ownerId, Guid? categoryId, Guid? coverMediaId, Guid? ogImageMediaId,
        CancellationToken cancellationToken)
    {
        if (categoryId is { } category && category != Guid.Empty
            && !await categories.AnyAsync(new MyActiveCategorySpec(category), cancellationToken))
            return PostErrors.CategoryNotFound;

        var mediaIds = new[] { coverMediaId, ogImageMediaId }
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (mediaIds.Count > 0)
        {
            var found = await media.CountAsync(new MediaByIdsForOwnerSpec(ownerId, mediaIds), cancellationToken);
            if (found != mediaIds.Count)
                return PostErrors.InvalidMediaReference;
        }

        return Result.Success();
    }

    /// <summary>
    /// Aniq berilgan slug band bo'lsa — Post.SlugTaken. Berilmagan bo'lsa sarlavhadan yaratiladi va
    /// band bo'lsa "-2", "-3" ... qo'shiladi.
    /// </summary>
    public static async Task<Result<string>> ResolveSlugAsync(IReadRepository<Post> posts, ISlugGenerator slugGenerator,
        Guid ownerId, string? requestedSlug, string title, Guid? exceptPostId, CancellationToken cancellationToken)
    {
        var requested = DomainRules.TrimToNull(requestedSlug)?.ToLowerInvariant();
        if (requested is not null)
        {
            if (!Post.IsValidSlug(requested))
                return PostErrors.SlugInvalid;

            var taken = await posts.ListAsync(new PostSlugsSpec(ownerId, requested, exceptPostId), cancellationToken);
            return taken.Contains(requested, StringComparer.Ordinal) ? PostErrors.SlugTaken : requested;
        }

        // Suffiks uchun joy qoldiramiz.
        var baseSlug = slugGenerator.Generate(title ?? string.Empty, PostConstraints.SlugMaxLength - 6);
        var existing = (await posts.ListAsync(new PostSlugsSpec(ownerId, baseSlug, exceptPostId), cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        if (!existing.Contains(baseSlug))
            return baseSlug;

        for (var i = 2; i < 100_000; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!existing.Contains(candidate))
                return candidate;
        }

        return PostErrors.SlugTaken;
    }

    public static Result<SeoMeta> CreateSeo(PostSeoInput? seo) =>
        seo is null
            ? SeoMeta.Empty
            : SeoMeta.Create(seo.MetaTitle, seo.MetaDescription, seo.OgImageMediaId, seo.CanonicalUrl);

    public static bool ContentDiffers(Post post, string title, PostContent content) =>
        !string.Equals(post.Title, title.Trim(), StringComparison.Ordinal)
        || !string.Equals(post.Content.Format, content.Format, StringComparison.Ordinal)
        || !string.Equals(post.Content.Html, content.Html, StringComparison.Ordinal)
        || !string.Equals(post.Content.Raw, content.Raw, StringComparison.Ordinal);
}

public sealed record PostSeoInput(string? MetaTitle, string? MetaDescription, Guid? OgImageMediaId, string? CanonicalUrl);
