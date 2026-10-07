using MyBlog.Domain.Categories;

namespace MyBlog.Application.Tests.Features.Categories;

internal static class CategoryTestData
{
    public static readonly Guid Owner = Guid.NewGuid();

    public static Category Create(string slug, Guid? parentId = null, int order = 0, string? ru = null)
    {
        List<CategoryTranslationData> translations = [new("uz", $"{slug} uz")];
        if (ru is not null)
            translations.Add(new CategoryTranslationData("ru", ru));

        return Category.Create(Owner, slug, parentId, translations, order).Value;
    }
}
