using System.Text.Json;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Posts.Common;

/// <summary>Muallif uchun to'liq PostDto yig'ish uchun kerakli manbalar (handler konstruktorida yaratiladi).</summary>
internal sealed record PostDtoSources(
    IReadRepository<Tag> Tags,
    IReadRepository<MediaFile> Media,
    IReadRepository<PostRevision> Revisions,
    IFileStorage Storage);

internal static class PostDtoBuilder
{
    public static async Task<PostDto> BuildAsync(Post post, uint version, PostDtoSources sources, CancellationToken cancellationToken)
    {
        var tagIds = post.Tags.Select(t => t.TagId).ToList();
        var tags = tagIds.Count == 0
            ? []
            : (await sources.Tags.ListAsync(new TagsByIdsSpec(tagIds), cancellationToken))
                .OrderBy(t => tagIds.IndexOf(t.Id))
                .Select(t => new PostTagDto(t.Id, t.Name, t.Slug))
                .ToList();

        string? coverUrl = null;
        if (post.CoverMediaId is { } coverId)
        {
            var cover = await sources.Media.ListAsync(new MediaByIdsSpec([coverId]), cancellationToken);
            coverUrl = cover.FirstOrDefault()?.GetUrl(sources.Storage, MediaVariant.Medium);
        }

        // Post oxirgi saqlangandan keyin yozilgan autosave bo'lsa — muharrir tiklashni taklif qiladi.
        var autosaves = await sources.Revisions.ListAsync(new RevisionHeadersSpec(post.Id, RevisionKind.Autosave), cancellationToken);
        var lastSaved = post.UpdatedAt ?? post.CreatedAt;
        var autosave = autosaves.Where(a => a.CreatedAt > lastSaved).MaxBy(a => a.Number);

        return new PostDto(
            post.Id,
            post.Title,
            post.Slug,
            post.Summary,
            post.Status,
            post.CategoryId,
            tags,
            post.CoverMediaId,
            coverUrl,
            ToContentDto(post.Content),
            ParseToc(post.Content.TableOfContentsJson),
            post.ReadingTimeMinutes,
            post.AllowComments,
            post.IsFeatured,
            new PostSeoDto(post.Seo.MetaTitle, post.Seo.MetaDescription, post.Seo.OgImageMediaId, post.Seo.CanonicalUrl),
            post.CreatedAt,
            post.UpdatedAt,
            post.PublishedAt,
            post.ScheduledAt,
            new PostCountsDto(post.ViewCount, post.LikeCount, post.DislikeCount, post.CommentCount),
            autosave is null ? null : new PostAutosaveInfoDto(autosave.Id, autosave.Title, autosave.CreatedAt),
            version);
    }

    public static PostContentDto ToContentDto(PostContent content) =>
        new(content.Format, content.Html, ParseRaw(content.Raw));

    public static IReadOnlyList<TocItem> ParseToc(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<TocItem>>(json, PostEditing.JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Raw JSON bo'lsa JSON qiymat sifatida, aks holda JSON string sifatida qaytariladi.</summary>
    private static JsonElement? ParseRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 256 });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(raw);
        }
    }
}

internal static class PostCache
{
    public static string Tag(Guid postId) => $"post:{postId}";

    public static string DetailKey(Guid postId, string culture) => $"post:{postId}:detail:{culture}";
}
