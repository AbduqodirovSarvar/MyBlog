using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.CreateComment;

/// <param name="ParentId">Javob bo'lsa ota izoh.</param>
public sealed record CreateCommentCommand(Guid PostId, string Content, Guid? ParentId = null) : ICommand<CommentDto>;

/// <summary>
/// Izoh darhol nashr qilinadi. Post nashr qilingan, o'chirilmagan va izohlarga ruxsat bergan bo'lishi kerak.
/// Post.CommentCount shu tranzaksiyada +1; bildirishnoma CommentCreatedDomainEvent orqali yuboriladi.
/// </summary>
internal sealed class CreateCommentCommandHandler(
    ICommentRepository comments,
    IReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IFileStorage fileStorage,
    IOptions<CommentsOptions> options,
    TimeProvider timeProvider) : ICommandHandler<CreateCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredId;
        var settings = options.Value;

        var post = await comments.GetPostInfoAsync(request.PostId, cancellationToken);
        if (post is null || !post.IsPublic)
            return CommentErrors.PostNotFound;
        if (!post.AllowComments)
            return CommentErrors.CommentsDisabled;

        var content = CommentContent.Validate(request.Content, settings.MaxLength);
        if (content.IsFailure)
            return content.Error;

        Comment? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await comments.GetIncludingDeletedAsync(parentId, cancellationToken);
            if (parent is null)
                return CommentErrors.ParentNotFound;

            // Domain MaxDepth'ni tekshiradi; sozlamada undan kichik chegara bo'lishi mumkin.
            if (parent.PostId == post.Id && !parent.IsDeleted && parent.Depth + 1 >= settings.MaxDepth)
                return CommentErrors.MaxDepthExceeded.WithArgs(settings.MaxDepth);
        }

        var created = Comment.Create(post.Id, userId, content.Value, parent);
        if (created.IsFailure)
            return created.Error;

        var comment = created.Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            comments.Add(comment);
            await comments.AdjustPostCommentCountAsync(post.Id, +1, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

        var view = await CommentView.LoadAsync(post, [comment], comments, reactions, currentUser, fileStorage, settings,
            timeProvider.GetUtcNow(), cancellationToken);

        return view.Map(comment);
    }
}
