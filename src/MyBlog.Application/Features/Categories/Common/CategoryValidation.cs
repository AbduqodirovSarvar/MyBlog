using FluentValidation;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using static MyBlog.Domain.Categories.CategoryConstraints;

namespace MyBlog.Application.Features.Categories.Common;

internal sealed class CategoryTranslationDataValidator : AbstractValidator<CategoryTranslationData>
{
    public CategoryTranslationDataValidator()
    {
        RuleFor(x => x.Culture)
            .Must(Cultures.IsSupported)
            .WithErrorCode(CategoryErrors.CultureNotSupported.Code)
            .WithMessage("This language is not supported.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);
    }
}

internal static class CategoryValidationRules
{
    /// <summary>Tarjimalar ro'yxati: bo'sh emas, "uz" bor, tillar takrorlanmaydi.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<CategoryTranslationData>> ValidTranslations<T>(
        this IRuleBuilder<T, IReadOnlyList<CategoryTranslationData>> rule) =>
        rule
            .NotEmpty()
            .Must(list => list is null || list.Any(t => Cultures.Normalize(t?.Culture) == Cultures.Default))
            .WithErrorCode(CategoryErrors.DefaultTranslationRequired.Code)
            .WithMessage(CategoryErrors.DefaultTranslationRequired.Description)
            .Must(list => list is null || list.Where(t => t is not null).GroupBy(t => Cultures.Normalize(t.Culture)).All(g => g.Count() == 1))
            .WithErrorCode("Category.DuplicateCulture")
            .WithMessage("Each language can be specified only once.");

    public static IRuleBuilderOptions<T, string?> ValidOptionalSlug<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(slug => DomainRules.TrimToNull(slug) is null || Category.IsValidSlug(slug!.Trim()))
            .WithErrorCode("Category.SlugFormatInvalid")
            .WithMessage("Slug may contain only lowercase latin letters, digits and hyphens.");
}
