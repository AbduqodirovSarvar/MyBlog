using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Media;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Posts.Common;

internal sealed record PublicPostSources(
    IReadRepository<UserProfile> Profiles,
    IReadRepository<Category> Categories,
    IReadRepository<Tag> Tags,
    IReadRepository<MediaFile> Media,
    IFileStorage Storage,
    ILocalizer Localizer);

/// <summary>Bir nechta post uchun muallif, kategoriya, teg va rasmlarni bir martada (N+1'siz) yuklaydi.</summary>
internal sealed class PublicPostLookups
{
    private readonly Dictionary<Guid, AuthorRow> _authors;
    private readonly Dictionary<Guid, Category> _categories;
    private readonly Dictionary<Guid, Tag> _tags;
    private readonly Dictionary<Guid, MediaFile> _media;
    private readonly IFileStorage _storage;
    private readonly string _culture;

    private PublicPostLookups(Dictionary<Guid, AuthorRow> authors, Dictionary<Guid, Category> categories,
        Dictionary<Guid, Tag> tags, Dictionary<Guid, MediaFile> media, IFileStorage storage, string culture)
    {
        _authors = authors;
        _categories = categories;
        _tags = tags;
        _media = media;
        _storage = storage;
        _culture = culture;
    }

    public static async Task<PublicPostLookups> LoadAsync(PublicPostSources sources, IReadOnlyCollection<Guid> authorIds,
        IReadOnlyCollection<Guid> categoryIds, IReadOnlyCollection<Guid> tagIds, IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        var authors = authorIds.Count == 0
            ? []
            : await sources.Profiles.ListAsync(new AuthorsByIdsSpec(authorIds.Distinct().ToList()), cancellationToken);
        var categories = categoryIds.Count == 0
            ? []
            : await sources.Categories.ListAsync(new CategoriesByIdsSpec(categoryIds.Distinct().ToList()), cancellationToken);
        var tags = tagIds.Count == 0
            ? []
            : await sources.Tags.ListAsync(new TagsByIdsSpec(tagIds.Distinct().ToList()), cancellationToken);

        var allMediaIds = mediaIds
            .Concat(authors.Where(a => a.AvatarMediaId is not null).Select(a => a.AvatarMediaId!.Value))
            .Distinct()
            .ToList();
        var media = allMediaIds.Count == 0
            ? []
            : await sources.Media.ListAsync(new MediaByIdsSpec(allMediaIds), cancellationToken);

        return new PublicPostLookups(
            authors.ToDictionary(a => a.Id),
            categories.ToDictionary(c => c.Id),
            tags.ToDictionary(t => t.Id),
            media.ToDictionary(m => m.Id),
            sources.Storage,
            sources.Localizer.CurrentCulture);
    }

    public PublicAuthorDto Author(Guid ownerId)
    {
        if (!_authors.TryGetValue(ownerId, out var author))
            return new PublicAuthorDto(string.Empty, string.Empty, null);

        return new PublicAuthorDto(author.Username, author.DisplayName, MediaUrl(author.AvatarMediaId, MediaVariant.Thumb));
    }

    public PublicCategoryDto? Category(Guid? categoryId) =>
        categoryId is { } id && _categories.TryGetValue(id, out var category) && category.IsActive
            ? new PublicCategoryDto(category.Slug, category.GetName(_culture))
            : null;

    public IReadOnlyList<PublicTagDto> Tags(IEnumerable<Guid> tagIds) =>
        tagIds
            .Where(_tags.ContainsKey)
            .Select(id => new PublicTagDto(_tags[id].Name, _tags[id].Slug))
            .ToList();

    public string? MediaUrl(Guid? mediaId, string? variant) =>
        mediaId is { } id && _media.TryGetValue(id, out var media) ? media.GetUrl(_storage, variant) : null;
}
