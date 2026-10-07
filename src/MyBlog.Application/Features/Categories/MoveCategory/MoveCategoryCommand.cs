using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Categories.MoveCategory;

/// <param name="NewParentId">null — ildizga.</param>
/// <param name="Order">Yangi ota ostidagi o'rni (0 dan); ro'yxat uzunligidan katta bo'lsa oxiriga.</param>
public sealed record MoveCategoryCommand(Guid Id, Guid? NewParentId, int Order) : ICommand;

internal sealed class MoveCategoryCommandValidator : AbstractValidator<MoveCategoryCommand>
{
    public MoveCategoryCommandValidator() => RuleFor(x => x.Order).GreaterThanOrEqualTo(0);
}

internal sealed class MoveCategoryCommandHandler(
    IRepository<Category> categories,
    IUnitOfWork unitOfWork,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<MoveCategoryCommand>
{
    public async Task<Result> Handle(MoveCategoryCommand request, CancellationToken cancellationToken)
    {
        // Foydalanuvchi kategoriyalari kam — butun daraxtni yuklab, tekshiruv va qayta raqamlashni xotirada qilamiz
        var all = await categories.ListAsync(new OwnCategoriesSpec(readOnly: false), cancellationToken);

        var result = Move(all, request.Id, request.NewParentId == Guid.Empty ? null : request.NewParentId, request.Order);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Tekshiradi, ko'chiradi va eski/yangi qo'shnilarni 0..n-1 qilib qayta raqamlaydi.</summary>
    internal static Result Move(IReadOnlyCollection<Category> all, Guid id, Guid? newParentId, int order)
    {
        var category = all.FirstOrDefault(c => c.Id == id);
        if (category is null)
            return CategoryErrors.NotFound;

        var check = new CategoryHierarchy(all.Select(c => (c.Id, c.ParentId))).CanMove(id, newParentId);
        if (check.IsFailure)
            return check;

        var oldParentId = category.ParentId;

        var newSiblings = all
            .Where(c => c.ParentId == newParentId && c.Id != id)
            .OrderBy(c => c.Order)
            .ToList();
        newSiblings.Insert(Math.Clamp(order, 0, newSiblings.Count), category);

        for (var i = 0; i < newSiblings.Count; i++)
        {
            var moved = newSiblings[i].MoveTo(newSiblings[i] == category ? newParentId : newSiblings[i].ParentId, i);
            if (moved.IsFailure)
                return moved;
        }

        if (oldParentId != newParentId)
        {
            var oldSiblings = all.Where(c => c.ParentId == oldParentId && c.Id != id).OrderBy(c => c.Order).ToList();
            for (var i = 0; i < oldSiblings.Count; i++)
                oldSiblings[i].MoveTo(oldParentId, i);
        }

        return Result.Success();
    }
}
