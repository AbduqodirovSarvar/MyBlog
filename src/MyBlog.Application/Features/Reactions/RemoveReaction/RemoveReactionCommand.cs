using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Application.Features.Reactions.Common;
using MyBlog.Application.Features.Reactions.SetReaction;
using MyBlog.Domain.Common;
using MyBlog.Domain.Reactions;

namespace MyBlog.Application.Features.Reactions.RemoveReaction;

/// <summary>Reaksiyani olib tashlaydi. Reaksiya bo'lmasa ham muvaffaqiyatli (idempotent).</summary>
public sealed record RemoveReactionCommand(ReactionTargetType TargetType, Guid TargetId) : ICommand<ReactionSummaryDto>;

internal sealed class RemoveReactionCommandHandler(
    IReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RemoveReactionCommand, ReactionSummaryDto>
{
    public async Task<Result<ReactionSummaryDto>> Handle(RemoveReactionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredId;

        if (!Enum.IsDefined(request.TargetType) || request.TargetId == Guid.Empty)
            return ReactionErrors.InvalidTarget;

        return await unitOfWork.ExecuteInTransactionAsync(
            ct => RemoveAsync(userId, request.TargetType, request.TargetId, ct), cancellationToken);
    }

    private async Task<Result<ReactionSummaryDto>> RemoveAsync(Guid userId, ReactionTargetType targetType, Guid targetId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < SetReactionCommandHandler.MaxAttempts; attempt++)
        {
            var existing = await reactions.FindAsync(userId, targetType, targetId, ct);
            if (existing is null)
                return await SummaryAsync(targetType, targetId, ct);

            // Parallel o'chirish/almashtirishda false — qayta o'qiymiz (hisoblagich ikki marta kamaymaydi).
            if (!await reactions.TryDeleteAsync(existing.Id, existing.Type, ct))
                continue;

            await reactions.AdjustCountersAsync(targetType, targetId,
                SetReactionCommandHandler.Delta(existing.Type, -1, ReactionType.Like),
                SetReactionCommandHandler.Delta(existing.Type, -1, ReactionType.Dislike), ct);
            return await SummaryAsync(targetType, targetId, ct);
        }

        return ReactionErrors.ConcurrentUpdate;
    }

    private async Task<Result<ReactionSummaryDto>> SummaryAsync(ReactionTargetType targetType, Guid targetId, CancellationToken ct)
    {
        var counts = await reactions.GetCountsAsync(targetType, targetId, ct);
        return counts is null
            ? ReactionErrors.TargetNotFound
            : new ReactionSummaryDto(counts.LikeCount, counts.DislikeCount, MyReaction: null);
    }
}
