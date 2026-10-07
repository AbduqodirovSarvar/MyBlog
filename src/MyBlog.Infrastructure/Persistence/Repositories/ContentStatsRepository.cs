using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Domain.Posts;

namespace MyBlog.Infrastructure.Persistence.Repositories;

/// <summary>
/// Post sonlari (GROUP BY). Ownership filtri chetlab o'tiladi va egasi aniq shart bilan beriladi;
/// SoftDelete filtri saqlanadi — o'chirilgan postlar hisoblanmaydi.
/// </summary>
internal sealed class ContentStatsRepository(AppDbContext dbContext) : IContentStatsRepository
{
    private IQueryable<Post> Posts => dbContext.Set<Post>().IgnoreQueryFilters([QueryFilterNames.Ownership]).AsNoTracking();

    public async Task<IReadOnlyDictionary<Guid, int>> CountPublishedPostsByOwnerAsync(IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken = default)
    {
        if (ownerIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await Posts
            .Where(p => ownerIds.Contains(p.OwnerId) && p.Status == PostStatus.Published)
            .GroupBy(p => p.OwnerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountPostsByCategoryAsync(Guid ownerId, bool publishedOnly,
        CancellationToken cancellationToken = default) =>
        await PostsOf(ownerId, publishedOnly)
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountPostsByTagAsync(Guid ownerId, bool publishedOnly,
        CancellationToken cancellationToken = default) =>
        await PostsOf(ownerId, publishedOnly)
            .SelectMany(p => p.Tags)
            .GroupBy(t => t.TagId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    private IQueryable<Post> PostsOf(Guid ownerId, bool publishedOnly) =>
        Posts.Where(p => p.OwnerId == ownerId && (!publishedOnly || p.Status == PostStatus.Published));
}
