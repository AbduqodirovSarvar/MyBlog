using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Categories.GetCategories;

/// <summary>Joriy foydalanuvchining kategoriya daraxti (faol bo'lmaganlari ham).</summary>
public sealed record GetCategoryTreeQuery : IQuery<IReadOnlyList<CategoryDto>>;

public sealed record GetCategoryQuery(Guid Id) : IQuery<CategoryDto>;

internal sealed class GetCategoryTreeQueryHandler(
    IReadRepository<Category> categories,
    IContentStatsRepository stats,
    ICurrentUser currentUser,
    ILocalizer localizer,
    IMediaUrlResolver mediaUrls) : IQueryHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(GetCategoryTreeQuery request,
        CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(new OwnCategoriesSpec(readOnly: true), cancellationToken);
        if (all.Count == 0)
            return Result.Success<IReadOnlyList<CategoryDto>>([]);

        var counts = await stats.CountPostsByCategoryAsync(currentUser.RequiredId, publishedOnly: false, cancellationToken);
        var icons = await mediaUrls.ResolveAsync(all.Select(c => c.IconMediaId), publicAccess: false,
            cancellationToken: cancellationToken);

        return Result.Success(CategoryMapper.ToTree(all, localizer.CurrentCulture, localizer.DefaultCulture, counts, icons));
    }
}

internal sealed class GetCategoryQueryHandler(
    IReadRepository<Category> categories,
    IReadRepository<Domain.Posts.Post> posts,
    ILocalizer localizer,
    IMediaUrlResolver mediaUrls) : IQueryHandler<GetCategoryQuery, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(GetCategoryQuery request, CancellationToken cancellationToken)
    {
        var category = await categories.FirstOrDefaultAsync(new CategoryByIdSpec(request.Id, readOnly: true),
            cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        var postCount = await posts.CountAsync(new PostsInCategorySpec(category.Id), cancellationToken);
        var icons = await mediaUrls.ResolveAsync([category.IconMediaId], publicAccess: false,
            cancellationToken: cancellationToken);

        return CategoryMapper.ToDto(category, localizer.CurrentCulture, localizer.DefaultCulture,
            new Dictionary<Guid, int> { [category.Id] = postCount }, icons);
    }
}
