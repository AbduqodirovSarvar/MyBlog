using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Posts.Public;

// ---------- Ro'yxat ----------

/// <param name="Author">Muallif username'i.</param>
/// <param name="Category">Kategoriya slug'i (muallif berilsa — uning kategoriyasi).</param>
/// <param name="Tag">Teg slug'i.</param>
/// <param name="Q">To'liq matnli qidiruv.</param>
/// <param name="Sort">newest | popular | mostLiked | relevance (q bo'lsa default — relevance).</param>
public sealed record ListPublicPostsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Author = null,
    string? Category = null,
    bool IncludeSubcategories = false,
    string? Tag = null,
    string? Q = null,
    bool? Featured = null,
    PublicPostSort? Sort = null) : IQuery<PagedList<PublicPostSummaryDto>>;

internal sealed class ListPublicPostsQueryHandler(
    IPostRepository postRepository,
    IReadRepository<UserProfile> profiles,
    IReadRepository<Category> categories,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IFileStorage storage,
    ILocalizer localizer)
    : IQueryHandler<ListPublicPostsQuery, PagedList<PublicPostSummaryDto>>
{
    private const int MaxSearchLength = 200;

    public async Task<Result<PagedList<PublicPostSummaryDto>>> Handle(ListPublicPostsQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);
        var empty = PagedList<PublicPostSummaryDto>.Empty(paging.SafePage, paging.SafePageSize);

        Guid? authorId = null;
        if (DomainRules.TrimToNull(request.Author) is { } username)
        {
            authorId = await profiles.FirstOrDefaultAsync(new AuthorIdByUsernameSpec(username), cancellationToken);
            if (authorId == Guid.Empty)
                return empty;
        }

        IReadOnlyCollection<Guid>? categoryIds = null;
        if (DomainRules.TrimToNull(request.Category) is { } categorySlug)
        {
            categoryIds = await ResolveCategoryIdsAsync(categorySlug, authorId, request.IncludeSubcategories, cancellationToken);
            if (categoryIds.Count == 0)
                return empty;
        }

        IReadOnlyCollection<Guid>? tagIds = null;
        if (DomainRules.TrimToNull(request.Tag) is { } tagSlug)
        {
            tagIds = await tags.ListAsync(new TagIdsBySlugSpec(tagSlug, authorId), cancellationToken);
            if (tagIds.Count == 0)
                return empty;
        }

        var search = DomainRules.TrimToNull(request.Q);
        if (search?.Length > MaxSearchLength)
            search = search[..MaxSearchLength];

        var sort = request.Sort ?? (search is null ? PublicPostSort.Newest : PublicPostSort.Relevance);
        if (sort == PublicPostSort.Relevance && search is null)
            sort = PublicPostSort.Newest;

        var filter = new PublicPostFilter(authorId, categoryIds, tagIds, search, request.Featured, sort);
        var page = await postRepository.ListPublishedAsync(filter, paging.SafePage, paging.SafePageSize, cancellationToken);
        if (page.Items.Count == 0)
            return new PagedList<PublicPostSummaryDto>([], page.Page, page.PageSize, page.TotalCount);

        var rows = page.Items;
        var lookups = await PublicPostLookups.LoadAsync(
            new PublicPostSources(profiles, categories, tags, media, storage, localizer),
            rows.Select(r => r.OwnerId).ToList(),
            rows.Where(r => r.CategoryId is not null).Select(r => r.CategoryId!.Value).ToList(),
            rows.SelectMany(r => r.TagIds).ToList(),
            rows.Where(r => r.CoverMediaId is not null).Select(r => r.CoverMediaId!.Value).ToList(),
            cancellationToken);

        var items = rows.Select(r => new PublicPostSummaryDto(
                r.Id, r.Title, r.Slug, r.Summary,
                lookups.MediaUrl(r.CoverMediaId, MediaVariant.Medium),
                r.PublishedAt, r.ReadingTimeMinutes, r.IsFeatured,
                lookups.Author(r.OwnerId),
                lookups.Category(r.CategoryId),
                lookups.Tags(r.TagIds),
                new PostCountsDto(r.ViewCount, r.LikeCount, r.DislikeCount, r.CommentCount)))
            .ToList();

        return new PagedList<PublicPostSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>Slug bo'yicha kategoriyalar; kerak bo'lsa ularning barcha avlodlari (daraxt xotirada yuriladi).</summary>
    private async Task<IReadOnlyCollection<Guid>> ResolveCategoryIdsAsync(string slug, Guid? authorId, bool includeSubcategories,
        CancellationToken cancellationToken)
    {
        var matched = await categories.ListAsync(new CategoryNodesBySlugSpec(slug, authorId), cancellationToken);
        if (matched.Count == 0 || !includeSubcategories)
            return matched.Select(c => c.Id).ToList();

        var owners = matched.Select(c => c.OwnerId).Distinct().ToList();
        var all = await categories.ListAsync(new CategoryNodesByOwnersSpec(owners), cancellationToken);
        var children = all.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value, c => c.Id);

        var result = new HashSet<Guid>(matched.Select(c => c.Id));
        var queue = new Queue<Guid>(result);
        while (queue.TryDequeue(out var id))
        {
            foreach (var child in children[id])
            {
                if (result.Add(child))
                    queue.Enqueue(child);
            }
        }

        return result;
    }
}

// ---------- Bitta post ----------

public sealed record GetPublicPostQuery(string Username, string Slug) : IQuery<PublicPostDetailDto>;

/// <summary>
/// Post sahifasi. Asosiy qismi keshlanadi (tag "post:{id}"); myReaction har so'rovda alohida olinadi.
/// ViewCount so'rov ichida atomar oshiriladi, xato bo'lsa so'rov yiqilmaydi.
/// </summary>
internal sealed class GetPublicPostQueryHandler(
    IReadRepository<Post> posts,
    IPostRepository postRepository,
    IReadRepository<UserProfile> profiles,
    IReadRepository<Category> categories,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IReadRepository<Reaction> reactions,
    IFileStorage storage,
    ILocalizer localizer,
    ICacheService cache,
    ICurrentUser currentUser,
    IOptions<PostsOptions> options,
    ILogger<GetPublicPostQueryHandler> logger)
    : IQueryHandler<GetPublicPostQuery, PublicPostDetailDto>
{
    public async Task<Result<PublicPostDetailDto>> Handle(GetPublicPostQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Slug))
            return PostErrors.NotFound;

        var authorId = await profiles.FirstOrDefaultAsync(new AuthorIdByUsernameSpec(request.Username), cancellationToken);
        if (authorId == Guid.Empty)
            return PostErrors.NotFound;

        var postId = await posts.FirstOrDefaultAsync(new PublishedPostIdSpec(authorId, request.Slug), cancellationToken);
        if (postId == Guid.Empty)
            return PostErrors.NotFound;

        var culture = localizer.CurrentCulture;
        var detail = await cache.GetOrCreateAsync(
            PostCache.DetailKey(postId, culture),
            ct => BuildAsync(postId, ct),
            TimeSpan.FromSeconds(options.Value.PublicDetailCacheSeconds),
            [PostCache.Tag(postId)],
            cancellationToken);

        if (detail is null)
            return PostErrors.NotFound;

        if (currentUser.Id is { } userId)
        {
            var reaction = await reactions.FirstOrDefaultAsync(new UserReactionSpec(userId, postId), cancellationToken);
            detail = detail with { MyReaction = reaction == default ? null : reaction };
        }

        try
        {
            await postRepository.IncrementViewCountAsync(postId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to increment view count of post {PostId}", postId);
        }

        return detail;
    }

    private async Task<PublicPostDetailDto?> BuildAsync(Guid postId, CancellationToken cancellationToken)
    {
        var post = await posts.FirstOrDefaultAsync(new PublishedPostByIdSpec(postId), cancellationToken);
        if (post is null)
            return null;

        var lookups = await PublicPostLookups.LoadAsync(
            new PublicPostSources(profiles, categories, tags, media, storage, localizer),
            [post.OwnerId],
            post.CategoryId is { } categoryId ? [categoryId] : [],
            post.Tags.Select(t => t.TagId).ToList(),
            new[] { post.CoverMediaId, post.Seo.OgImageMediaId }.OfType<Guid>().ToList(),
            cancellationToken);

        PostNavLinkDto? previous = null, next = null;
        if (post.PublishedAt is { } publishedAt)
        {
            previous = await posts.FirstOrDefaultAsync(new AdjacentPostSpec(post.OwnerId, post.Id, publishedAt, older: true), cancellationToken);
            next = await posts.FirstOrDefaultAsync(new AdjacentPostSpec(post.OwnerId, post.Id, publishedAt, older: false), cancellationToken);
        }

        var coverUrl = lookups.MediaUrl(post.CoverMediaId, MediaVariant.Large);
        var seo = new PublicPostSeoDto(
            post.Seo.MetaTitle ?? post.Title,
            post.Seo.MetaDescription ?? post.Summary,
            post.Seo.CanonicalUrl,
            lookups.MediaUrl(post.Seo.OgImageMediaId, MediaVariant.Large) ?? coverUrl);

        return new PublicPostDetailDto(
            post.Id,
            post.Title,
            post.Slug,
            post.Summary,
            post.Content.Html,
            PostDtoBuilder.ParseToc(post.Content.TableOfContentsJson),
            coverUrl,
            post.PublishedAt,
            post.UpdatedAt,
            post.ReadingTimeMinutes,
            post.AllowComments,
            post.IsFeatured,
            seo,
            lookups.Author(post.OwnerId),
            lookups.Category(post.CategoryId),
            lookups.Tags(post.Tags.Select(t => t.TagId)),
            new PostCountsDto(post.ViewCount, post.LikeCount, post.DislikeCount, post.CommentCount),
            previous,
            next,
            MyReaction: null);
    }
}
