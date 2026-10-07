using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments.ListComments;

/// <summary>Joriy foydalanuvchi yozgan izohlar (yangisi birinchi), post sarlavhasi/slug/muallifi bilan.</summary>
public sealed record GetMyCommentsQuery(int Page = 1, int PageSize = 20) : IQuery<PagedList<CommentListItemDto>>;

/// <summary>Admin: barcha o'chirilmagan izohlar, matn yoki muallif username'i bo'yicha qidiruv.</summary>
public sealed record GetAdminCommentsQuery(string? Search = null, int Page = 1, int PageSize = 20)
    : IQuery<PagedList<CommentListItemDto>>;

internal sealed class GetMyCommentsQueryHandler(
    ICommentRepository comments,
    ICurrentUser currentUser,
    IPublicContentPolicy contentPolicy) : IQueryHandler<GetMyCommentsQuery, PagedList<CommentListItemDto>>
{
    public async Task<Result<PagedList<CommentListItemDto>>> Handle(GetMyCommentsQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);

        // Yopiq tizimda boshqalarning postlari (sarlavha, slug, muallif) ko'rsatilmaydi — faqat o'z postlaridagi izohlar.
        var filter = new CommentListFilter(AuthorId: currentUser.RequiredId, PostOwnerId: contentPolicy.OwnerScope);
        var rows = await comments.ListAsync(filter, paging.SafePage, paging.SafePageSize, cancellationToken);

        return CommentListMapping.Map(rows);
    }
}

internal sealed class GetAdminCommentsQueryHandler(ICommentRepository comments)
    : IQueryHandler<GetAdminCommentsQuery, PagedList<CommentListItemDto>>
{
    private const int MaxSearchLength = 100;

    public async Task<Result<PagedList<CommentListItemDto>>> Handle(GetAdminCommentsQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);
        var search = DomainRules.TrimToNull(request.Search);
        if (search?.Length > MaxSearchLength)
            search = search[..MaxSearchLength];

        var rows = await comments.ListAsync(new CommentListFilter(Search: search),
            paging.SafePage, paging.SafePageSize, cancellationToken);

        return CommentListMapping.Map(rows);
    }
}

internal static class CommentListMapping
{
    public static PagedList<CommentListItemDto> Map(PagedList<CommentListRow> rows) =>
        new(rows.Items.Select(r => new CommentListItemDto(
                r.Id,
                r.ParentId,
                r.Content,
                r.IsEdited,
                r.CreatedAt,
                r.LikeCount,
                r.DislikeCount,
                new CommentPostRefDto(r.PostId, r.PostTitle, r.PostSlug, r.PostAuthorUsername),
                new CommentAuthorDto(r.AuthorId, r.AuthorUsername, r.AuthorDisplayName, AvatarUrl: null)))
            .ToList(),
            rows.Page, rows.PageSize, rows.TotalCount);
}
