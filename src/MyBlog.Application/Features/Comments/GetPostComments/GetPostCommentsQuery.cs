using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.GetPostComments;

/// <summary>Ildiz izohlar sahifasi; har biri to'liq javoblar daraxti bilan.</summary>
public sealed record GetPostCommentsQuery(Guid PostId, int Page = 1, int PageSize = 20, CommentSort Sort = CommentSort.Oldest)
    : IQuery<PagedList<CommentDto>>;

/// <summary>
/// So'rovlar: post (1), ildizlar sahifasi + count (2), ildizlarning barcha avlodlari (1), mualliflar (1),
/// joriy foydalanuvchi reaksiyalari (1). Izohlar soni sahifadagidan qat'i nazar o'zgarmas.
/// </summary>
internal sealed class GetPostCommentsQueryHandler(
    ICommentRepository comments,
    IReactionRepository reactions,
    ICurrentUser currentUser,
    IFileStorage fileStorage,
    IOptions<CommentsOptions> options,
    TimeProvider timeProvider,
    IPublicContentPolicy contentPolicy) : IQueryHandler<GetPostCommentsQuery, PagedList<CommentDto>>
{
    public async Task<Result<PagedList<CommentDto>>> Handle(GetPostCommentsQuery request, CancellationToken cancellationToken)
    {
        var post = await comments.GetPostInfoAsync(request.PostId, cancellationToken);
        if (!CanView(post, currentUser, contentPolicy))
            return CommentErrors.PostNotFound;

        var paging = new PageRequest(request.Page, request.PageSize);
        var sort = Enum.IsDefined(request.Sort) ? request.Sort : CommentSort.Oldest;

        var roots = await comments.GetRootPageAsync(post!.Id, sort, paging.SafePage, paging.SafePageSize, cancellationToken);
        if (roots.Items.Count == 0)
            return new PagedList<CommentDto>([], roots.Page, roots.PageSize, roots.TotalCount);

        var replies = await comments.GetRepliesAsync(post.Id, roots.Items.Select(r => r.Id).ToList(), cancellationToken);

        var view = await CommentView.LoadAsync(post, [.. roots.Items, .. replies], comments, reactions, currentUser,
            fileStorage, options.Value, timeProvider.GetUtcNow(), cancellationToken);

        return new PagedList<CommentDto>(view.BuildTree(roots.Items, replies), roots.Page, roots.PageSize, roots.TotalCount);
    }

    /// <summary>
    /// Ommaviy post — hamma uchun; nashr qilinmagan — faqat egasi va moderator uchun.
    /// Yopiq tizimda (PublicReadOfPublishedContent=false) boshqaning posti umuman ko'rinmaydi.
    /// </summary>
    internal static bool CanView(CommentPostInfo? post, ICurrentUser currentUser, IPublicContentPolicy contentPolicy) =>
        post is { IsDeleted: false }
        && contentPolicy.CanRead(post.OwnerId)
        && (post.IsPublic || post.OwnerId == currentUser.Id || CommentRules.CanModerate(currentUser));
}
