using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Categories.DeleteCategory;

/// <summary>Soft delete. Ichki kategoriyasi yoki posti bo'lsa rad etiladi.</summary>
public sealed record DeleteCategoryCommand(Guid Id) : ICommand;

internal sealed class DeleteCategoryCommandHandler(
    IRepository<Category> categories,
    IReadRepository<Post> posts,
    IUnitOfWork unitOfWork,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.FirstOrDefaultAsync(new CategoryByIdSpec(request.Id), cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        if (await categories.AnyAsync(new CategoryChildrenSpec(category.Id), cancellationToken))
            return CategoryErrors.HasChildren;

        if (await posts.AnyAsync(new PostsInCategorySpec(category.Id), cancellationToken))
            return CategoryErrors.HasPosts;

        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);
        return Result.Success();
    }
}
