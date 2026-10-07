using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.DeleteComment;

public sealed record DeleteCommentCommand(Guid CommentId) : ICommand;

/// <summary>
/// Soft delete. Ruxsat: muallif, post egasi yoki Comments.Moderate. Post.CommentCount shu tranzaksiyada −1.
/// Javoblari bor izoh ro'yxatda placeholder bo'lib qoladi.
/// </summary>
internal sealed class DeleteCommentCommandHandler(
    ICommentRepository comments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<DeleteCommentCommand>
{
    public async Task<Result> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredId;

        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return CommentErrors.NotFound;

        var post = await comments.GetPostInfoAsync(comment.PostId, cancellationToken);
        if (post is null)
            return CommentErrors.NotFound;

        if (!CommentRules.CanDelete(comment, userId, post.OwnerId, CommentRules.CanModerate(currentUser)))
            return CommentErrors.DeleteForbidden;

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            comments.Remove(comment);
            await comments.AdjustPostCommentCountAsync(comment.PostId, -1, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}
