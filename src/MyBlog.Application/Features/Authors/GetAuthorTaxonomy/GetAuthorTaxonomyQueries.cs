using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Tags.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Authors.GetAuthorTaxonomy;

/// <summary>Muallifning faol kategoriyalari daraxti, joriy tilda, nashr qilingan postlar soni bilan.</summary>
public sealed record GetAuthorCategoriesQuery(string Username) : IQuery<IReadOnlyList<PublicCategoryDto>>;

/// <summary>Muallifning kamida bitta nashr qilingan posti bor teglari.</summary>
public sealed record GetAuthorTagsQuery(string Username) : IQuery<IReadOnlyList<PublicTagDto>>;

internal sealed class GetAuthorCategoriesQueryHandler(
    IReadRepository<UserProfile> profiles,
    IReadRepository<Category> categories,
    IContentStatsRepository stats,
    IMediaUrlResolver mediaUrls,
    ILocalizer localizer,
    ICacheService cache) : IQueryHandler<GetAuthorCategoriesQuery, IReadOnlyList<PublicCategoryDto>>
{
    public async Task<Result<IReadOnlyList<PublicCategoryDto>>> Handle(GetAuthorCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var author = await AuthorLookup.FindAsync(profiles, request.Username, cancellationToken);
        if (author is null)
            return UserProfileErrors.NotFound;

        var culture = localizer.CurrentCulture;
        var tree = await cache.GetOrCreateAsync(
            AuthorCacheKeys.Categories(author.Username, culture),
            async ct =>
            {
                var active = await categories.ListAsync(new PublicCategoriesSpec(author.Id), ct);
                var counts = await stats.CountPostsByCategoryAsync(author.Id, publishedOnly: true, ct);
                var icons = await mediaUrls.ResolveAsync(active.Select(c => c.IconMediaId), publicAccess: true,
                    cancellationToken: ct);
                return CategoryMapper.ToPublicTree(active, culture, localizer.DefaultCulture, counts, icons).ToList();
            },
            AuthorCacheKeys.Expiration,
            [AuthorCacheKeys.Tag(author.Username)],
            cancellationToken);

        return tree;
    }
}

internal sealed class GetAuthorTagsQueryHandler(
    IReadRepository<UserProfile> profiles,
    IReadRepository<Tag> tags,
    IContentStatsRepository stats,
    ICacheService cache) : IQueryHandler<GetAuthorTagsQuery, IReadOnlyList<PublicTagDto>>
{
    public async Task<Result<IReadOnlyList<PublicTagDto>>> Handle(GetAuthorTagsQuery request,
        CancellationToken cancellationToken)
    {
        var author = await AuthorLookup.FindAsync(profiles, request.Username, cancellationToken);
        if (author is null)
            return UserProfileErrors.NotFound;

        var list = await cache.GetOrCreateAsync(
            AuthorCacheKeys.Tags(author.Username),
            async ct =>
            {
                var counts = await stats.CountPostsByTagAsync(author.Id, publishedOnly: true, ct);
                if (counts.Count == 0)
                    return new List<PublicTagDto>();

                var all = await tags.ListAsync(new PublicTagsSpec(author.Id), ct);
                return all
                    .Where(t => counts.GetValueOrDefault(t.Id) > 0)
                    .Select(t => new PublicTagDto(t.Name, t.Slug, counts[t.Id]))
                    .ToList();
            },
            AuthorCacheKeys.Expiration,
            [AuthorCacheKeys.Tag(author.Username)],
            cancellationToken);

        return list;
    }
}

internal static class AuthorLookup
{
    public static Task<PublicProfileRef?> FindAsync(IReadRepository<UserProfile> profiles, string? username,
        CancellationToken cancellationToken) =>
        UserProfile.IsValidUsername(username?.Trim())
            ? profiles.FirstOrDefaultAsync(new PublicProfileIdByUsernameSpec(username!), cancellationToken)
            : Task.FromResult<PublicProfileRef?>(null);
}
