using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Tags.Common;

public sealed record TagDto(Guid Id, string Name, string Slug, int PostCount);

public sealed record PublicTagDto(string Name, string Slug, int PostCount);

/// <summary>Joriy foydalanuvchining teglari (ixtiyoriy qidiruv bilan).</summary>
internal sealed class OwnTagsSpec : Specification<Tag>
{
    public OwnTagsSpec(string? search)
    {
        if (search?.Trim().ToLowerInvariant() is { Length: > 0 } term)
            Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term));

        OrderByAsc(t => t.Name);
        ReadOnly();
    }
}

internal sealed class TagByIdSpec : Specification<Tag>
{
    public TagByIdSpec(Guid id) => Where(t => t.Id == id);
}

internal sealed class TagSlugTakenSpec : Specification<Tag>
{
    public TagSlugTakenSpec(string slug, Guid? exceptId) =>
        Where(t => t.Slug == slug && (exceptId == null || t.Id != exceptId));
}

internal sealed record TagRef(Guid Id, string Slug);

/// <summary>Egasining berilgan slug'li teglari.</summary>
internal sealed class TagsBySlugsSpec : Specification<Tag, TagRef>
{
    public TagsBySlugsSpec(Guid ownerId, IReadOnlyCollection<string> slugs)
    {
        Where(t => t.OwnerId == ownerId && slugs.Contains(t.Slug));
        Select(t => new TagRef(t.Id, t.Slug));
        ReadOnly();
    }
}

/// <summary>Muallifning teglari (ommaviy).</summary>
internal sealed class PublicTagsSpec : Specification<Tag>
{
    public PublicTagsSpec(Guid ownerId)
    {
        IgnoreOwnership();
        Where(t => t.OwnerId == ownerId);
        OrderByAsc(t => t.Name);
        ReadOnly();
    }
}
