using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Posts.Manage;

// ---------- Bitta post (muharrir uchun: raw bilan) ----------

public sealed record GetMyPostQuery(Guid Id) : IQuery<PostDto>;

internal sealed class GetMyPostQueryHandler(
    IReadRepository<Post> posts,
    IPostRepository postRepository,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IReadRepository<PostRevision> revisions,
    IFileStorage storage)
    : IQueryHandler<GetMyPostQuery, PostDto>
{
    public async Task<Result<PostDto>> Handle(GetMyPostQuery request, CancellationToken cancellationToken)
    {
        // Versiyani o'qish uchun entity kuzatilishi kerak (xmin shadow property).
        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.Id), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        return await PostDtoBuilder.BuildAsync(post, postRepository.GetVersion(post),
            new PostDtoSources(tags, media, revisions, storage), cancellationToken);
    }
}

// ---------- Ro'yxat ----------

public sealed record ListMyPostsQuery(
    int Page = 1,
    int PageSize = 20,
    PostStatus? Status = null,
    Guid? CategoryId = null,
    Guid? TagId = null,
    string? Search = null,
    MyPostSort Sort = MyPostSort.Updated,
    bool Descending = true) : IQuery<PagedList<MyPostListItemDto>>;

internal sealed class ListMyPostsQueryHandler(IReadRepository<Post> posts, IReadRepository<MediaFile> media, IFileStorage storage)
    : IQueryHandler<ListMyPostsQuery, PagedList<MyPostListItemDto>>
{
    public async Task<Result<PagedList<MyPostListItemDto>>> Handle(ListMyPostsQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);

        var total = await posts.CountAsync(new MyPostsSpec(request.Status, request.CategoryId, request.TagId, request.Search,
            request.Sort, request.Descending, 1, 1, forCount: true), cancellationToken);
        if (total == 0)
            return PagedList<MyPostListItemDto>.Empty(paging.SafePage, paging.SafePageSize);

        var rows = await posts.ListAsync(new MyPostsSpec(request.Status, request.CategoryId, request.TagId, request.Search,
            request.Sort, request.Descending, paging.SafePage, paging.SafePageSize), cancellationToken);

        var coverIds = rows.Where(r => r.CoverMediaId is not null).Select(r => r.CoverMediaId!.Value).Distinct().ToList();
        var covers = coverIds.Count == 0
            ? new Dictionary<Guid, MediaFile>()
            : (await media.ListAsync(new MediaByIdsSpec(coverIds), cancellationToken)).ToDictionary(m => m.Id);

        var items = rows.Select(r => new MyPostListItemDto(
                r.Id, r.Title, r.Slug, r.Status, r.CategoryId,
                r.CoverMediaId is { } c && covers.TryGetValue(c, out var cover) ? cover.GetUrl(storage, MediaVariant.Thumb) : null,
                r.IsFeatured, r.ReadingTimeMinutes, r.CreatedAt, r.UpdatedAt, r.PublishedAt, r.ScheduledAt,
                new PostCountsDto(r.ViewCount, r.LikeCount, r.DislikeCount, r.CommentCount)))
            .ToList();

        return new PagedList<MyPostListItemDto>(items, paging.SafePage, paging.SafePageSize, total);
    }
}
