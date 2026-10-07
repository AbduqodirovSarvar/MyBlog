using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.EditComment;

public sealed record EditCommentCommand(Guid CommentId, string Content) : ICommand<CommentDto>;

/// <summary>Faqat muallif (moderator ham emas) va EditWindowMinutes ichida tahrirlay oladi.</summary>
internal sealed class EditCommentCommandHandler(
    ICommentRepository comments,
    IReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IFileStorage fileStorage,
    IOptions<CommentsOptions> options,
    TimeProvider timeProvider) : ICommandHandler<EditCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(EditCommentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredId;
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return CommentErrors.NotFound;
        if (!comment.IsAuthoredBy(userId))
            return CommentErrors.NotAuthor;

        var post = await comments.GetPostInfoAsync(comment.PostId, cancellationToken);
        if (post is null || !post.IsPublic)
            return CommentErrors.PostNotFound;

        if (!CommentRules.IsWithinEditWindow(comment, settings, now))
            return CommentErrors.EditWindowExpired.WithArgs(settings.EditWindowMinutes);

        var content = CommentContent.Validate(request.Content, settings.MaxLength);
        if (content.IsFailure)
            return content.Error;

        var edited = comment.Edit(content.Value, now);
        if (edited.IsFailure)
            return edited.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await CommentView.LoadAsync(post, [comment], comments, reactions, currentUser, fileStorage, settings,
            now, cancellationToken);

        return view.Map(comment);
    }
}
