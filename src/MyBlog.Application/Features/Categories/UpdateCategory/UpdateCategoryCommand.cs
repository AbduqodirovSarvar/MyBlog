using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Categories.UpdateCategory;

/// <summary>
/// Tarjimalar to'liq almashtiriladi (ro'yxatda yo'q tillar o'chiriladi, "uz" majburiy).
/// <paramref name="Slug"/> null bo'lsa o'zgarmaydi.
/// </summary>
public sealed record UpdateCategoryCommand(
    Guid Id,
    IReadOnlyList<CategoryTranslationData> Translations,
    string? Slug,
    Guid? IconMediaId,
    bool IsActive) : ICommand<CategoryDto>;

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Translations).ValidTranslations();
        RuleForEach(x => x.Translations).SetValidator(new CategoryTranslationDataValidator());
        RuleFor(x => x.Slug).ValidOptionalSlug();
    }
}

internal sealed class UpdateCategoryCommandHandler(
    IRepository<Category> categories,
    IReadRepository<MediaFile> mediaFiles,
    IReadRepository<Post> posts,
    IUnitOfWork unitOfWork,
    ILocalizer localizer,
    IMediaUrlResolver mediaUrls,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.FirstOrDefaultAsync(new CategoryByIdSpec(request.Id), cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        var translations = ReplaceTranslations(category, request.Translations);
        if (translations.IsFailure)
            return translations.Error;

        if (DomainRules.TrimToNull(request.Slug) is { } slug && slug != category.Slug)
        {
            var taken = await categories.AnyAsync(new CategorySlugTakenSpec(slug, category.Id), cancellationToken);
            if (taken)
                return CategoryErrors.SlugTaken;

            var changed = category.ChangeSlug(slug);
            if (changed.IsFailure)
                return changed.Error;
        }

        var iconMediaId = request.IconMediaId == Guid.Empty ? null : request.IconMediaId;
        if (iconMediaId != category.IconMediaId)
        {
            if (iconMediaId is { } iconId && await mediaFiles.GetByIdAsync(iconId, cancellationToken) is null)
                return MediaErrors.NotFound;
            category.SetIcon(iconMediaId);
        }

        if (request.IsActive)
            category.Activate();
        else
            category.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);

        var postCount = await posts.CountAsync(new PostsInCategorySpec(category.Id), cancellationToken);
        var icons = await mediaUrls.ResolveAsync([category.IconMediaId], publicAccess: false, cancellationToken: cancellationToken);
        return CategoryMapper.ToDto(category, localizer.CurrentCulture, localizer.DefaultCulture,
            new Dictionary<Guid, int> { [category.Id] = postCount }, icons);
    }

    internal static Result ReplaceTranslations(Category category, IReadOnlyList<CategoryTranslationData> translations)
    {
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var translation in translations)
        {
            var culture = Cultures.Normalize(translation.Culture);
            if (culture is null)
                return CategoryErrors.CultureNotSupportedFor(translation.Culture ?? string.Empty);
            if (!requested.Add(culture))
                return CategoryErrors.DuplicateTranslationFor(culture);
        }

        if (!requested.Contains(Cultures.Default))
            return CategoryErrors.DefaultTranslationRequired;

        foreach (var translation in translations)
        {
            var result = category.SetTranslation(translation.Culture, translation.Name, translation.Description);
            if (result.IsFailure)
                return result;
        }

        foreach (var culture in category.Translations.Select(t => t.Culture).Where(c => !requested.Contains(c)).ToList())
        {
            var result = category.RemoveTranslation(culture);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }
}

/// <summary>Joriy foydalanuvchida shu slug'li boshqa kategoriya bormi.</summary>
internal sealed class CategorySlugTakenSpec : Specification<Category>
{
    public CategorySlugTakenSpec(string slug, Guid exceptId) => Where(c => c.Slug == slug && c.Id != exceptId);
}
