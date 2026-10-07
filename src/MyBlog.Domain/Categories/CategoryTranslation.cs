namespace MyBlog.Domain.Categories;

/// <summary>Kategoriyaning bitta tildagi nomi. Kalit: (CategoryId, Culture).</summary>
public sealed class CategoryTranslation
{
    private CategoryTranslation() { }

    internal CategoryTranslation(Guid categoryId, string culture, string name, string? description)
    {
        CategoryId = categoryId;
        Culture = culture;
        Name = name;
        Description = description;
    }

    public Guid CategoryId { get; private set; }
    public string Culture { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    internal void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}

/// <summary>Create/Update uchun kiruvchi tarjima ma'lumoti.</summary>
public sealed record CategoryTranslationData(string Culture, string Name, string? Description = null);
