using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Repositories;
using Npgsql;

namespace MyBlog.Infrastructure.Reactions;

/// <summary>
/// Reaksiyalar. Hisoblagichlar ExecuteUpdate bilan atomar o'zgaradi; yozish metodlari shartli bo'lib,
/// parallel so'rovlar natijasini (unique violation, 0 ta qator) false sifatida qaytaradi.
/// </summary>
internal sealed class ReactionRepository(AppDbContext dbContext) : EfRepository<Reaction>(dbContext), IReactionRepository
{
    // Post owned entity — ommaviy target bo'lgani uchun Ownership filtri o'chiriladi.
    private IQueryable<Post> Posts => DbContext.Set<Post>().IgnoreQueryFilters([QueryFilterNames.Ownership]);

    private IQueryable<Comment> Comments => DbContext.Set<Comment>();

    public Task<Reaction?> FindAsync(Guid userId, ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default) =>
        Set.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.TargetType == targetType && r.TargetId == targetId,
                cancellationToken);

    public Task<bool> IsTargetAvailableAsync(ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default) =>
        targetType switch
        {
            ReactionTargetType.Post => Posts.AnyAsync(p => p.Id == targetId && p.Status == PostStatus.Published, cancellationToken),
            ReactionTargetType.Comment => Comments.AnyAsync(c => c.Id == targetId
                && Posts.Any(p => p.Id == c.PostId && p.Status == PostStatus.Published), cancellationToken),
            _ => Task.FromResult(false)
        };

    public Task<Guid?> GetTargetOwnerIdAsync(ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default) =>
        targetType switch
        {
            ReactionTargetType.Post => Posts.Where(p => p.Id == targetId)
                .Select(p => (Guid?)p.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            ReactionTargetType.Comment => Comments.Where(c => c.Id == targetId)
                .Join(Posts, c => c.PostId, p => p.Id, (_, p) => (Guid?)p.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            _ => Task.FromResult<Guid?>(null)
        };

    public async Task<bool> TryInsertAsync(Reaction reaction, CancellationToken cancellationToken = default)
    {
        Set.Add(reaction);
        try
        {
            // Tranzaksiya ichida EF savepoint qo'yadi: xatolikdan keyin tranzaksiya ishlatishda davom etadi.
            await DbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            DbContext.Entry(reaction).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryChangeTypeAsync(Guid reactionId, ReactionType expected, ReactionType newType, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await Set
            .Where(r => r.Id == reactionId && r.Type == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Type, newType)
                .SetProperty(r => r.UpdatedAt, now.ToUniversalTime()), cancellationToken) == 1;

    public async Task<bool> TryDeleteAsync(Guid reactionId, ReactionType expected, CancellationToken cancellationToken = default) =>
        await Set
            .Where(r => r.Id == reactionId && r.Type == expected)
            .ExecuteDeleteAsync(cancellationToken) == 1;

    public Task AdjustCountersAsync(ReactionTargetType targetType, Guid targetId, int likeDelta, int dislikeDelta,
        CancellationToken cancellationToken = default)
    {
        if (likeDelta == 0 && dislikeDelta == 0)
            return Task.CompletedTask;

        return targetType switch
        {
            ReactionTargetType.Post => DbContext.Set<Post>()
                .IgnoreQueryFilters([QueryFilterNames.Ownership, QueryFilterNames.SoftDelete])
                .Where(p => p.Id == targetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LikeCount, p => p.LikeCount + likeDelta < 0 ? 0 : p.LikeCount + likeDelta)
                    .SetProperty(p => p.DislikeCount, p => p.DislikeCount + dislikeDelta < 0 ? 0 : p.DislikeCount + dislikeDelta),
                    cancellationToken),
            ReactionTargetType.Comment => DbContext.Set<Comment>()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(c => c.Id == targetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.LikeCount, c => c.LikeCount + likeDelta < 0 ? 0 : c.LikeCount + likeDelta)
                    .SetProperty(c => c.DislikeCount, c => c.DislikeCount + dislikeDelta < 0 ? 0 : c.DislikeCount + dislikeDelta),
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unknown reaction target.")
        };
    }

    public Task<ReactionCounts?> GetCountsAsync(ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default) =>
        targetType switch
        {
            ReactionTargetType.Post => Posts
                .Where(p => p.Id == targetId)
                .Select(p => new ReactionCounts(p.LikeCount, p.DislikeCount))
                .FirstOrDefaultAsync(cancellationToken),
            ReactionTargetType.Comment => Comments
                .Where(c => c.Id == targetId)
                .Select(c => new ReactionCounts(c.LikeCount, c.DislikeCount))
                .FirstOrDefaultAsync(cancellationToken),
            _ => Task.FromResult<ReactionCounts?>(null)
        };

    public async Task<IReadOnlyDictionary<Guid, ReactionType>> GetUserReactionsAsync(Guid userId, ReactionTargetType targetType,
        IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken = default)
    {
        if (targetIds.Count == 0)
            return new Dictionary<Guid, ReactionType>();

        var ids = targetIds.Distinct().ToList();
        return await Set
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TargetType == targetType && ids.Contains(r.TargetId))
            .ToDictionaryAsync(r => r.TargetId, r => r.Type, cancellationToken);
    }
}
