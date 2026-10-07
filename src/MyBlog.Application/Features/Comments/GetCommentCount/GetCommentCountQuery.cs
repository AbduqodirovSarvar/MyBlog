using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Comments.GetPostComments;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.GetCommentCount;

/// <summary>Post'dagi o'chirilmagan izohlar soni (denormalizatsiya qilingan Post.CommentCount).</summary>
public sealed record GetCommentCountQuery(Guid PostId) : IQuery<CommentCountDto>;

internal sealed class GetCommentCountQueryHandler(
    ICommentRepository comments,
    ICurrentUser currentUser,
    IPublicContentPolicy contentPolicy) : IQueryHandler<GetCommentCountQuery, CommentCountDto>
{
    public async Task<Result<CommentCountDto>> Handle(GetCommentCountQuery request, CancellationToken cancellationToken)
    {
        var post = await comments.GetPostInfoAsync(request.PostId, cancellationToken);
        if (!GetPostCommentsQueryHandler.CanView(post, currentUser, contentPolicy))
            return CommentErrors.PostNotFound;

        return new CommentCountDto(post!.Id, post.CommentCount);
    }
}
