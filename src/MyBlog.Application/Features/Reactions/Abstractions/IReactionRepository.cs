using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Reactions;

namespace MyBlog.Application.Features.Reactions.Abstractions;

public sealed record ReactionCounts(int LikeCount, int DislikeCount);

/// <summary>
/// Reaksiyalar va target hisoblagichlari. Barcha o'zgartiruvchi metodlar shartli (optimistik) ishlaydi:
/// false qaytsa — parallel so'rov ulgurgan, chaqiruvchi holatni qayta o'qib urinadi.
/// </summary>
public interface IReactionRepository : IRepository<Reaction>
{
    /// <summary>Foydalanuvchining target'ga reaksiyasi (tracking'siz).</summary>
    Task<Reaction?> FindAsync(Guid userId, ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Post nashr qilingan va o'chirilmagan / izoh o'chirilmagan va uning posti ommaviy.</summary>
    Task<bool> IsTargetAvailableAsync(ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Darhol saqlaydi. Unique (user, target) buzilsa false (parallel birinchi reaksiya).</summary>
    Task<bool> TryInsertAsync(Reaction reaction, CancellationToken cancellationToken = default);

    /// <summary>Faqat joriy turi <paramref name="expected"/> bo'lsa almashtiradi.</summary>
    Task<bool> TryChangeTypeAsync(Guid reactionId, ReactionType expected, ReactionType newType, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Faqat joriy turi <paramref name="expected"/> bo'lsa o'chiradi.</summary>
    Task<bool> TryDeleteAsync(Guid reactionId, ReactionType expected, CancellationToken cancellationToken = default);

    /// <summary>Target'ning LikeCount/DislikeCount'ini atomar o'zgartiradi (SET like_count = like_count + delta).</summary>
    Task AdjustCountersAsync(ReactionTargetType targetType, Guid targetId, int likeDelta, int dislikeDelta,
        CancellationToken cancellationToken = default);

    /// <summary>Target topilmasa null.</summary>
    Task<ReactionCounts?> GetCountsAsync(ReactionTargetType targetType, Guid targetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, ReactionType>> GetUserReactionsAsync(Guid userId, ReactionTargetType targetType,
        IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken = default);
}
