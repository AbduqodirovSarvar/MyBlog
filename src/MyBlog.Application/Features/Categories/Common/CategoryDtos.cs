namespace MyBlog.Application.Features.Categories.Common;

public sealed record CategoryTranslationDto(string Culture, string Name, string? Description);

/// <summary>
/// Boshqaruv uchun kategoriya: <see cref="Name"/> joriy tilda (fallback bilan), <see cref="Translations"/> — tahrirlash uchun hammasi.
/// </summary>
public sealed record CategoryDto(
    Guid Id,
    Guid? ParentId,
    string Slug,
    string Name,
    string? Description,
    int Order,
    Guid? IconMediaId,
    string? IconUrl,
    bool IsActive,
    int PostCount,
    IReadOnlyList<CategoryTranslationDto> Translations,
    IReadOnlyList<CategoryDto> Children);

/// <summary>Ommaviy kategoriya daraxti (faqat faol, joriy tilda).</summary>
/// <param name="PostCount">Shu kategoriyadagi nashr qilingan postlar.</param>
/// <param name="TotalPostCount">Ichki kategoriyalar bilan birga.</param>
public sealed record PublicCategoryDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    string? IconUrl,
    int PostCount,
    int TotalPostCount,
    IReadOnlyList<PublicCategoryDto> Children);
