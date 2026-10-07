using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Application.Features.Reactions.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Reactions;

namespace MyBlog.Application.Features.Reactions.SetReaction;

/// <summary>Reaksiya qo'yadi yoki almashtiradi (Like ↔ Dislike). Xuddi shu reaksiyani qayta qo'yish — no-op.</summary>
public sealed record SetReactionCommand(ReactionTargetType TargetType, Guid TargetId, ReactionType Type)
    : ICommand<ReactionSummaryDto>;

/// <summary>
/// Reaksiya yozuvi va target hisoblagichlari bitta tranzaksiyada o'zgaradi. Hisoblagichlar atomar
/// (SET like_count = like_count + 1). Parallel so'rovlar: unique index buzilsa yoki tur o'zgarib qolsa
/// holat qayta o'qilib yana urinib ko'riladi (switch yoki no-op bo'ladi); bir necha urinishdan keyin Conflict.
/// </summary>
internal sealed class SetReactionCommandHandler(
    IReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IPublicContentPolicy contentPolicy) : ICommandHandler<SetReactionCommand, ReactionSummaryDto>
{
    internal const int MaxAttempts = 3;

    public async Task<Result<ReactionSummaryDto>> Handle(SetReactionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredId;

        if (!Enum.IsDefined(request.TargetType) || request.TargetId == Guid.Empty)
            return ReactionErrors.InvalidTarget;
        if (!Enum.IsDefined(request.Type))
            return ReactionErrors.InvalidType;

        if (!await reactions.IsTargetAvailableAsync(request.TargetType, request.TargetId, cancellationToken)
            || !await IsTargetReadableAsync(reactions, contentPolicy, request.TargetType, request.TargetId, cancellationToken))
            return ReactionErrors.TargetNotFound;

        return await unitOfWork.ExecuteInTransactionAsync(
            ct => ApplyAsync(userId, request, ct), cancellationToken);
    }

    private async Task<Result<ReactionSummaryDto>> ApplyAsync(Guid userId, SetReactionCommand request, CancellationToken ct)
    {
        var (targetType, targetId, type) = (request.TargetType, request.TargetId, request.Type);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var existing = await reactions.FindAsync(userId, targetType, targetId, ct);

            if (existing is null)
            {
                var created = Reaction.Create(userId, targetType, targetId, type, now);
                if (created.IsFailure)
                    return created.Error;

                // Parallel birinchi reaksiya: unique index → qayta o'qib, switch/no-op sifatida davom etamiz.
                if (!await reactions.TryInsertAsync(created.Value, ct))
                    continue;

                await reactions.AdjustCountersAsync(targetType, targetId, Delta(type, +1, ReactionType.Like),
                    Delta(type, +1, ReactionType.Dislike), ct);
                return await SummaryAsync(targetType, targetId, type, ct);
            }

            if (existing.Type == type)
                return await SummaryAsync(targetType, targetId, type, ct);

            var previous = existing.Type;
            var changed = existing.ChangeType(type, now);
            if (changed.IsFailure)
                return changed.Error;

            if (!await reactions.TryChangeTypeAsync(existing.Id, previous, type, now, ct))
                continue;

            await reactions.AdjustCountersAsync(targetType, targetId,
                Delta(type, +1, ReactionType.Like) + Delta(previous, -1, ReactionType.Like),
                Delta(type, +1, ReactionType.Dislike) + Delta(previous, -1, ReactionType.Dislike), ct);
            return await SummaryAsync(targetType, targetId, type, ct);
        }

        return ReactionErrors.ConcurrentUpdate;
    }

    private async Task<Result<ReactionSummaryDto>> SummaryAsync(ReactionTargetType targetType, Guid targetId,
        ReactionType? myReaction, CancellationToken ct)
    {
        var counts = await reactions.GetCountsAsync(targetType, targetId, ct);
        return counts is null
            ? ReactionErrors.TargetNotFound
            : new ReactionSummaryDto(counts.LikeCount, counts.DislikeCount, myReaction?.ToString());
    }

    internal static int Delta(ReactionType type, int sign, ReactionType counter) => type == counter ? sign : 0;

    /// <summary>Yopiq tizimda faqat o'z postiga (yoki o'z postidagi izohga) reaksiya; ochiq tizimda qo'shimcha so'rov yo'q.</summary>
    internal static async Task<bool> IsTargetReadableAsync(IReactionRepository reactions, IPublicContentPolicy contentPolicy,
        ReactionTargetType targetType, Guid targetId, CancellationToken cancellationToken)
    {
        if (contentPolicy.CanReadOthersContent)
            return true;

        var ownerId = await reactions.GetTargetOwnerIdAsync(targetType, targetId, cancellationToken);
        return ownerId is { } owner && contentPolicy.CanRead(owner);
    }
}
