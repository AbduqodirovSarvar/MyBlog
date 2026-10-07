using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Categories.CreateCategory;

/// <param name="Slug">Ixtiyoriy; berilmasa "uz" nomidan yaratiladi (band bo'lsa -2, -3 ...).</param>
public sealed record CreateCategoryCommand(
    IReadOnlyList<CategoryTranslationData> Translations,
    Guid? ParentId = null,
    string? Slug = null,
    Guid? IconMediaId = null,
    bool IsActive = true) : ICommand<CategoryDto>;

internal sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Translations).ValidTranslations();
        RuleForEach(x => x.Translations).SetValidator(new CategoryTranslationDataValidator());
        RuleFor(x => x.Slug).ValidOptionalSlug();
    }
}

internal sealed class CreateCategoryCommandHandler(
    IRepository<Category> categories,
    IReadRepository<MediaFile> mediaFiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ISlugGenerator slugGenerator,
    ILocalizer localizer,
    IMediaUrlResolver mediaUrls,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.RequiredId;
        var nodes = await categories.ListAsync(new OwnCategoryNodesSpec(), cancellationToken);
        var parentId = request.ParentId == Guid.Empty ? null : request.ParentId;

        var placement = new CategoryHierarchy(nodes.Select(n => (n.Id, n.ParentId))).CanAddChild(parentId);
        if (placement.IsFailure)
            return placement.Error;

        var slug = ResolveSlug(request, nodes);
        if (slug.IsFailure)
            return slug.Error;

        var iconMediaId = request.IconMediaId == Guid.Empty ? null : request.IconMediaId;
        if (iconMediaId is { } iconId && await mediaFiles.GetByIdAsync(iconId, cancellationToken) is null)
            return MediaErrors.NotFound;

        var siblings = nodes.Where(n => n.ParentId == parentId).ToList();
        var order = siblings.Count == 0 ? 0 : siblings.Max(n => n.Order) + 1;

        var created = Category.Create(ownerId, slug.Value, parentId, request.Translations, order, iconMediaId);
        if (created.IsFailure)
            return created.Error;

        var category = created.Value;
        if (!request.IsActive)
            category.Deactivate();

        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);

        var icons = await mediaUrls.ResolveAsync([category.IconMediaId], publicAccess: false, cancellationToken: cancellationToken);
        return CategoryMapper.ToDto(category, localizer.CurrentCulture, localizer.DefaultCulture,
            new Dictionary<Guid, int>(), icons);
    }

    private Result<string> ResolveSlug(CreateCategoryCommand request, IReadOnlyCollection<CategoryNode> nodes)
    {
        var taken = nodes.Select(n => n.Slug).ToHashSet(StringComparer.Ordinal);

        if (DomainRules.TrimToNull(request.Slug) is { } explicitSlug)
            return taken.Contains(explicitSlug) ? Result.Failure<string>(CategoryErrors.SlugTaken) : explicitSlug;

        var defaultName = request.Translations
            .First(t => Cultures.Normalize(t.Culture) == Cultures.Default)
            .Name;
        var baseSlug = slugGenerator.Generate(defaultName, CategoryConstraints.SlugMaxLength);
        return UniqueSlug.Resolve(baseSlug, taken, CategoryConstraints.SlugMaxLength);
    }
}
