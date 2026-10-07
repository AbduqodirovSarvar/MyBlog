using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Categories.Common;

internal static class CategoryMapper
{
    public static CategoryDto ToDto(Category category, string culture, string fallbackCulture,
        IReadOnlyDictionary<Guid, int> postCounts, IReadOnlyDictionary<Guid, string> iconUrls,
        IReadOnlyList<CategoryDto>? children = null)
    {
        var translation = category.GetTranslation(culture, fallbackCulture);

        return new CategoryDto(
            category.Id,
            category.ParentId,
            category.Slug,
            translation?.Name ?? category.Slug,
            translation?.Description,
            category.Order,
            category.IconMediaId,
            iconUrls.UrlFor(category.IconMediaId),
            category.IsActive,
            postCounts.GetValueOrDefault(category.Id),
            category.Translations
                .OrderBy(t => CultureOrder(t.Culture))
                .Select(t => new CategoryTranslationDto(t.Culture, t.Name, t.Description))
                .ToList(),
            children ?? []);
    }

    /// <summary>Boshqaruv daraxti: ota-bola bog'lanishi, har bir darajada Order va nom bo'yicha.</summary>
    public static IReadOnlyList<CategoryDto> ToTree(IReadOnlyCollection<Category> categories, string culture,
        string fallbackCulture, IReadOnlyDictionary<Guid, int> postCounts, IReadOnlyDictionary<Guid, string> iconUrls)
    {
        var ids = categories.Select(c => c.Id).ToHashSet();
        var byParent = categories.ToLookup(c => c.ParentId is { } p && ids.Contains(p) ? p : (Guid?)null);

        IReadOnlyList<CategoryDto> Build(Guid? parentId, int depth) =>
            byParent[parentId]
                .OrderBy(c => c.Order)
                .ThenBy(c => c.GetName(culture, fallbackCulture), StringComparer.CurrentCultureIgnoreCase)
                .Select(c => ToDto(c, culture, fallbackCulture, postCounts, iconUrls,
                    depth < CategoryConstraints.MaxDepth * 2 ? Build(c.Id, depth + 1) : []))
                .ToList();

        return Build(null, 1);
    }

    /// <summary>Ommaviy daraxt: faqat berilgan (faol) kategoriyalar; faol bo'lmagan ota ostidagilar ko'rinmaydi.</summary>
    public static IReadOnlyList<PublicCategoryDto> ToPublicTree(IReadOnlyCollection<Category> activeCategories,
        string culture, string fallbackCulture, IReadOnlyDictionary<Guid, int> postCounts,
        IReadOnlyDictionary<Guid, string> iconUrls)
    {
        var byParent = activeCategories.ToLookup(c => c.ParentId);

        IReadOnlyList<PublicCategoryDto> Build(Guid? parentId, int depth) =>
            byParent[parentId]
                .OrderBy(c => c.Order)
                .ThenBy(c => c.GetName(culture, fallbackCulture), StringComparer.CurrentCultureIgnoreCase)
                .Select(c =>
                {
                    var children = depth < CategoryConstraints.MaxDepth * 2 ? Build(c.Id, depth + 1) : [];
                    var translation = c.GetTranslation(culture, fallbackCulture);
                    var own = postCounts.GetValueOrDefault(c.Id);
                    return new PublicCategoryDto(
                        c.Id,
                        c.Slug,
                        translation?.Name ?? c.Slug,
                        translation?.Description,
                        iconUrls.UrlFor(c.IconMediaId),
                        own,
                        own + children.Sum(ch => ch.TotalPostCount),
                        children);
                })
                .ToList();

        return Build(null, 1);
    }

    private static int CultureOrder(string culture)
    {
        var index = Cultures.Supported.ToList().IndexOf(culture);
        return index < 0 ? int.MaxValue : index;
    }
}
