using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Repositories;

namespace MyBlog.Infrastructure.Comments;

/// <summary>
/// Izohlar repository'si. Post va UserProfile ommaviy o'qilganda Ownership filtri o'chiriladi
/// (SoftDelete saqlanadi, kerak bo'lgan joyda aniq ko'rsatilgan).
/// </summary>
internal sealed class CommentRepository(AppDbContext dbContext) : EfRepository<Comment>(dbContext), ICommentRepository
{
    private const char LikeEscape = '\\';

    private IQueryable<Comment> WithDeleted => Set.IgnoreQueryFilters([QueryFilterNames.SoftDelete]);

    private IQueryable<Post> Posts => DbContext.Set<Post>().IgnoreQueryFilters([QueryFilterNames.Ownership]);

    private IQueryable<UserProfile> Profiles => DbContext.Set<UserProfile>().IgnoreQueryFilters([QueryFilterNames.Ownership]);

    public Task<Comment?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDeleted.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<CommentPostInfo?> GetPostInfoAsync(Guid postId, CancellationToken cancellationToken = default) =>
        (from post in DbContext.Set<Post>().IgnoreQueryFilters([QueryFilterNames.Ownership, QueryFilterNames.SoftDelete])
         join owner in Profiles on post.OwnerId equals owner.Id
         where post.Id == postId
         select new CommentPostInfo(post.Id, post.OwnerId, owner.Username, post.Title, post.Slug, post.Status,
             post.AllowComments, post.IsDeleted, post.CommentCount))
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedList<Comment>> GetRootPageAsync(Guid postId, CommentSort sort, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = WithDeleted;

        // O'chirilgan ildiz faqat o'chirilmagan avlodi (javob yoki javobga javob) bo'lsa ko'rinadi.
        var roots = all.Where(c => c.PostId == postId && c.ParentId == null
            && (!c.IsDeleted
                || all.Any(r => r.PostId == postId && !r.IsDeleted
                    && (r.ParentId == c.Id || all.Any(m => m.Id == r.ParentId && m.ParentId == c.Id)))));

        var total = await roots.CountAsync(cancellationToken);
        if (total == 0)
            return PagedList<Comment>.Empty(page, pageSize);

        IOrderedQueryable<Comment> ordered = sort switch
        {
            CommentSort.Newest => roots.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id),
            CommentSort.Top => roots.OrderByDescending(c => c.LikeCount).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id),
            _ => roots.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedList<Comment>(items, page, pageSize, total);
    }

    public Task<List<Comment>> GetRepliesAsync(Guid postId, IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken = default)
    {
        if (rootIds.Count == 0)
            return Task.FromResult(new List<Comment>());

        var all = WithDeleted;
        var ids = rootIds.ToList();

        // Depth <= 2 (check constraint): ildizning bolalari va nevaralari yetarli.
        return all
            .Where(c => c.PostId == postId && c.ParentId != null
                && (ids.Contains(c.ParentId.Value)
                    || all.Any(p => p.Id == c.ParentId && p.ParentId != null && ids.Contains(p.ParentId.Value))))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, CommentUserInfo>> GetUsersAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, CommentUserInfo>();

        var profiles = await Profiles
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Username, p.DisplayName, p.AvatarMediaId, p.PreferredCulture, p.NotifyOnComment, p.NotifyOnReply
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var avatarIds = profiles.Where(p => p.AvatarMediaId is not null).Select(p => p.AvatarMediaId!.Value).Distinct().ToList();
        var avatarKeys = new Dictionary<Guid, string>();
        if (avatarIds.Count > 0)
        {
            var media = await DbContext.Set<MediaFile>()
                .IgnoreQueryFilters([QueryFilterNames.Ownership])
                .Where(m => avatarIds.Contains(m.Id))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var file in media)
                avatarKeys[file.Id] = file.Variants.FirstOrDefault(v => v.Name == MediaVariant.Thumb)?.StorageKey ?? file.StorageKey;
        }

        return profiles.ToDictionary(
            p => p.Id,
            p => new CommentUserInfo(p.Id, p.Username, p.DisplayName,
                p.AvatarMediaId is { } mediaId ? avatarKeys.GetValueOrDefault(mediaId) : null,
                p.PreferredCulture, p.NotifyOnComment, p.NotifyOnReply));
    }

    public Task AdjustPostCommentCountAsync(Guid postId, int delta, CancellationToken cancellationToken = default)
    {
        if (delta == 0)
            return Task.CompletedTask;

        return DbContext.Set<Post>()
            .IgnoreQueryFilters([QueryFilterNames.Ownership, QueryFilterNames.SoftDelete])
            .Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                p => p.CommentCount,
                p => p.CommentCount + delta < 0 ? 0 : p.CommentCount + delta), cancellationToken);
    }

    public async Task<PagedList<CommentListRow>> ListAsync(CommentListFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query =
            from comment in Set
            join post in Posts on comment.PostId equals post.Id
            join author in Profiles on comment.AuthorId equals author.Id
            join owner in Profiles on post.OwnerId equals owner.Id
            select new { comment, post, author, owner };

        if (filter.AuthorId is { } authorId)
            query = query.Where(x => x.comment.AuthorId == authorId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.comment.Content, pattern, LikeEscape.ToString())
                || EF.Functions.ILike(x.author.Username, pattern, LikeEscape.ToString()));
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedList<CommentListRow>.Empty(page, pageSize);

        var items = await query
            .OrderByDescending(x => x.comment.CreatedAt).ThenByDescending(x => x.comment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CommentListRow(
                x.comment.Id, x.comment.PostId, x.comment.ParentId, x.comment.Content, x.comment.IsEdited,
                x.comment.CreatedAt, x.comment.LikeCount, x.comment.DislikeCount,
                x.post.Title, x.post.Slug, x.owner.Username,
                x.author.Id, x.author.Username, x.author.DisplayName))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedList<CommentListRow>(items, page, pageSize, total);
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
