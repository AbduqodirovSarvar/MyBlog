using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Domain.Posts;
using MyBlog.Infrastructure.Persistence.Configurations;
using MyBlog.Infrastructure.Persistence.Configurations.Posts;
using NpgsqlTypes;

namespace MyBlog.Infrastructure.Persistence.Repositories;

internal sealed class PostRepository(AppDbContext dbContext) : EfRepository<Post>(dbContext), IPostRepository
{
    private const string SearchConfiguration = "simple";

    public uint GetVersion(Post post) =>
        DbContext.Entry(post).Property<uint>(ConfigurationConstants.VersionProperty).CurrentValue;

    public void SetExpectedVersion(Post post, uint version) =>
        DbContext.Entry(post).Property<uint>(ConfigurationConstants.VersionProperty).OriginalValue = version;

    public Task IncrementViewCountAsync(Guid postId, CancellationToken cancellationToken = default) =>
        // Anonim o'quvchi uchun ham ishlashi kerak — ownership filtri chetlab o'tiladi (soft delete qoladi).
        Set.IgnoreQueryFilters([QueryFilterNames.Ownership])
            .Where(p => p.Id == postId && p.Status == PostStatus.Published)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), cancellationToken);

    public Task DeleteRevisionsAsync(IReadOnlyCollection<Guid> revisionIds, CancellationToken cancellationToken = default)
    {
        if (revisionIds.Count == 0)
            return Task.CompletedTask;

        var ids = revisionIds.ToList();
        // Ownership filtri qoladi: faqat joriy foydalanuvchining reviziyalari o'chadi.
        return DbContext.Set<PostRevision>().Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<PagedList<PublicPostRow>> ListPublishedAsync(PublicPostFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = FilterPublished(filter);
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedList<PublicPostRow>.Empty(page, pageSize);

        var rows = await PagePublished(query, filter, page, pageSize).ToListAsync(cancellationToken);
        return new PagedList<PublicPostRow>(rows, page, pageSize, total);
    }

    internal IQueryable<Post> FilterPublished(PublicPostFilter filter)
    {
        var query = Set.IgnoreQueryFilters([QueryFilterNames.Ownership])
            .AsNoTracking()
            .Where(p => p.Status == PostStatus.Published);

        if (filter.AuthorId is { } authorId)
            query = query.Where(p => p.OwnerId == authorId);

        if (filter.CategoryIds is { Count: > 0 } categoryIds)
        {
            var ids = categoryIds.ToList();
            query = query.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }

        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            var ids = tagIds.ToList();
            query = query.Where(p => p.Tags.Any(t => ids.Contains(t.TagId)));
        }

        if (filter.Featured is { } featured)
            query = query.Where(p => p.IsFeatured == featured);

        var search = filter.Search;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => EF.Property<NpgsqlTsVector>(p, PostConfiguration.SearchVectorProperty)
                .Matches(EF.Functions.PlainToTsQuery(SearchConfiguration, search)));
        }

        return query;
    }

    internal static IQueryable<PublicPostRow> PagePublished(IQueryable<Post> query, PublicPostFilter filter, int page, int pageSize)
    {
        var search = filter.Search;
        var ordered = filter.Sort switch
        {
            PublicPostSort.Relevance when !string.IsNullOrWhiteSpace(search) => query
                .OrderByDescending(p => EF.Property<NpgsqlTsVector>(p, PostConfiguration.SearchVectorProperty)
                    .Rank(EF.Functions.PlainToTsQuery(SearchConfiguration, search)))
                .ThenByDescending(p => p.PublishedAt),
            PublicPostSort.Popular => query.OrderByDescending(p => p.ViewCount).ThenByDescending(p => p.PublishedAt),
            PublicPostSort.MostLiked => query.OrderByDescending(p => p.LikeCount).ThenByDescending(p => p.PublishedAt),
            _ => query.OrderByDescending(p => p.PublishedAt)
        };

        return ordered
            .ThenByDescending(p => p.Id)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PublicPostRow(
                p.Id, p.OwnerId, p.CategoryId, p.CoverMediaId, p.Title, p.Slug, p.Summary, p.PublishedAt,
                p.ReadingTimeMinutes, p.IsFeatured, p.ViewCount, p.LikeCount, p.DislikeCount, p.CommentCount,
                p.Tags.Select(t => t.TagId).ToList()));
    }
}
