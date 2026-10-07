using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Comments.GetPostComments;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.GetComment;

/// <summary>Bitta izoh (javoblarsiz) — permalink va 201 Location uchun.</summary>
public sealed record GetCommentQuery(Guid CommentId) : IQuery<CommentDto>;

internal sealed class GetCommentQueryHandler(
    ICommentRepository comments,
    IReactionRepository reactions,
    ICurrentUser currentUser,
    IFileStorage fileStorage,
    IOptions<CommentsOptions> options,
    TimeProvider timeProvider,
    IPublicContentPolicy contentPolicy) : IQueryHandler<GetCommentQuery, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(GetCommentQuery request, CancellationToken cancellationToken)
    {
        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return CommentErrors.NotFound;

        var post = await comments.GetPostInfoAsync(comment.PostId, cancellationToken);
        if (!GetPostCommentsQueryHandler.CanView(post, currentUser, contentPolicy))
            return CommentErrors.NotFound;

        var view = await CommentView.LoadAsync(post!, [comment], comments, reactions, currentUser, fileStorage,
            options.Value, timeProvider.GetUtcNow(), cancellationToken);

        return view.Map(comment);
    }
}
