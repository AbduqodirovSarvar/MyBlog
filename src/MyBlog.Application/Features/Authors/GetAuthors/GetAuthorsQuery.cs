using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Authors.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Authors.GetAuthors;

public sealed record GetAuthorsQuery(string? Search = null, int Page = 1, int PageSize = 20)
    : IQuery<PagedList<AuthorSummaryDto>>;

internal sealed class GetAuthorsQueryHandler(
    IReadRepository<UserProfile> profiles,
    IContentStatsRepository stats,
    IMediaUrlResolver mediaUrls,
    IPublicContentPolicy contentPolicy) : IQueryHandler<GetAuthorsQuery, PagedList<AuthorSummaryDto>>
{
    public async Task<Result<PagedList<AuthorSummaryDto>>> Handle(GetAuthorsQuery request,
        CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);

        // Yopiq tizim: faqat o'z profili (anonim — Guid.Empty, ya'ni bo'sh ro'yxat).
        var onlyId = contentPolicy.OwnerScope;

        var total = await profiles.CountAsync(new AuthorsSpec(request.Search, onlyId: onlyId), cancellationToken);
        if (total == 0)
            return PagedList<AuthorSummaryDto>.Empty(paging.SafePage, paging.SafePageSize);

        var items = await profiles.ListAsync(
            new AuthorsSpec(request.Search, paging.SafePage, paging.SafePageSize, onlyId), cancellationToken);

        var counts = await stats.CountPublishedPostsByOwnerAsync(items.Select(i => i.Id).ToList(), cancellationToken);
        var avatars = await mediaUrls.ResolveAsync(items.Select(i => i.AvatarMediaId), publicAccess: true,
            ProfileMapper.AvatarVariant, cancellationToken);

        var dtos = items
            .Select(i => new AuthorSummaryDto(i.Username, i.DisplayName, avatars.UrlFor(i.AvatarMediaId), i.Bio,
                counts.GetValueOrDefault(i.Id)))
            .ToList();

        return new PagedList<AuthorSummaryDto>(dtos, paging.SafePage, paging.SafePageSize, total);
    }
}
