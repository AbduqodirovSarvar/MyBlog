using MyBlog.Domain.Common;
using static MyBlog.Domain.Categories.CategoryConstraints;

namespace MyBlog.Domain.Categories;

public static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("Category.NotFound", "Category was not found.");
    public static readonly Error ParentNotFound = Error.NotFound("Category.ParentNotFound", "Parent category was not found.");
    public static readonly Error InvalidOwner = Error.Validation("Category.InvalidOwner", "Category owner is required.");

    public static readonly Error SlugInvalid = Error.Validation("Category.SlugInvalid",
            "Slug must contain only lowercase latin letters, digits and hyphens and must not exceed {0} characters.")
        .WithArgs(SlugMaxLength);

    public static readonly Error SlugTaken = Error.Conflict("Category.SlugTaken", "A category with the same slug already exists.");

    public static readonly Error DefaultTranslationRequired = Error.Validation("Category.DefaultTranslationRequired",
            "Translation for the default culture '{0}' is required.")
        .WithArgs(Cultures.Default);

    public static readonly Error CannotRemoveDefaultTranslation = Error.Validation("Category.CannotRemoveDefaultTranslation",
            "Translation for the default culture '{0}' cannot be removed.")
        .WithArgs(Cultures.Default);

    public static readonly Error CultureNotSupported = Error.Validation("Category.CultureNotSupported", "Culture '{0}' is not supported.");
    public static readonly Error DuplicateTranslation = Error.Validation("Category.DuplicateTranslation", "Translation for culture '{0}' is specified more than once.");
    public static readonly Error TranslationNotFound = Error.NotFound("Category.TranslationNotFound", "Translation for culture '{0}' was not found.");
    public static readonly Error NameRequired = Error.Validation("Category.NameRequired", "Category name is required.");

    public static readonly Error NameTooLong = Error.Validation("Category.NameTooLong", "Category name must not exceed {0} characters.")
        .WithArgs(NameMaxLength);

    public static readonly Error DescriptionTooLong = Error.Validation("Category.DescriptionTooLong", "Category description must not exceed {0} characters.")
        .WithArgs(DescriptionMaxLength);

    public static readonly Error CannotBeOwnParent = Error.Validation("Category.CannotBeOwnParent", "A category cannot be its own parent.");
    public static readonly Error CircularReference = Error.Validation("Category.CircularReference", "A category cannot be moved under its own descendant.");

    public static readonly Error MaxDepthExceeded = Error.Validation("Category.MaxDepthExceeded", "Category nesting cannot exceed {0} levels.")
        .WithArgs(MaxDepth);

    public static readonly Error HasChildren = Error.Conflict("Category.HasChildren", "Category has subcategories and cannot be deleted.");
    public static readonly Error OrderInvalid = Error.Validation("Category.OrderInvalid", "Order must be zero or a positive number.");

    public static Error CultureNotSupportedFor(string culture) => CultureNotSupported.WithArgs(culture);
    public static Error DuplicateTranslationFor(string culture) => DuplicateTranslation.WithArgs(culture);
    public static Error TranslationNotFoundFor(string culture) => TranslationNotFound.WithArgs(culture);
}
