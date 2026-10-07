using MyBlog.Domain.Common;
using static MyBlog.Domain.Categories.CategoryConstraints;

namespace MyBlog.Domain.Categories;

/// <summary>
/// Foydalanuvchining shaxsiy kategoriyasi (daraxt). Nomi 4 tilda saqlanadi, "uz" tarjimasi majburiy.
/// Maksimal chuqurlik (<see cref="CategoryConstraints.MaxDepth"/>) va slug unikalligi Application qatlamida tekshiriladi.
/// </summary>
public sealed class Category : SoftDeletableEntity, IAggregateRoot, IOwnedEntity
{
    private readonly List<CategoryTranslation> _translations = [];

    private Category() { }

    private Category(Guid ownerId, string slug, Guid? parentId, int order, Guid? iconMediaId)
    {
        OwnerId = ownerId;
        Slug = slug;
        ParentId = parentId;
        Order = order;
        IconMediaId = iconMediaId;
        IsActive = true;
    }

    public Guid OwnerId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Slug { get; private set; } = null!;
    public int Order { get; private set; }
    public Guid? IconMediaId { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<CategoryTranslation> Translations => _translations.AsReadOnly();

    public static Result<Category> Create(Guid ownerId, string slug, Guid? parentId,
        IEnumerable<CategoryTranslationData> translations, int order = 0, Guid? iconMediaId = null)
    {
        if (ownerId == Guid.Empty)
            return CategoryErrors.InvalidOwner;

        var slugValue = slug?.Trim();
        if (!IsValidSlug(slugValue))
            return CategoryErrors.SlugInvalid;
        if (order < 0)
            return CategoryErrors.OrderInvalid;

        var category = new Category(ownerId, slugValue!, NormalizeId(parentId), order, NormalizeId(iconMediaId));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var translation in translations)
        {
            var culture = Cultures.Normalize(translation.Culture);
            if (culture is null)
                return CategoryErrors.CultureNotSupportedFor(translation.Culture ?? string.Empty);
            if (!seen.Add(culture))
                return CategoryErrors.DuplicateTranslationFor(culture);

            var result = category.SetTranslation(culture, translation.Name, translation.Description);
            if (result.IsFailure)
                return result.Error;
        }

        if (!seen.Contains(Cultures.Default))
            return CategoryErrors.DefaultTranslationRequired;

        return category;
    }

    public static bool IsValidSlug(string? slug) => slug is { Length: <= SlugMaxLength } && DomainRules.IsValidSlug(slug);

    /// <summary>Tarjimani qo'shadi yoki mavjudini yangilaydi.</summary>
    public Result SetTranslation(string culture, string name, string? description)
    {
        var cultureValue = Cultures.Normalize(culture);
        if (cultureValue is null)
            return CategoryErrors.CultureNotSupportedFor(culture ?? string.Empty);

        var nameValue = DomainRules.TrimToNull(name);
        var descriptionValue = DomainRules.TrimToNull(description);
        if (nameValue is null)
            return CategoryErrors.NameRequired;
        if (nameValue.Length > NameMaxLength)
            return CategoryErrors.NameTooLong;
        if (descriptionValue?.Length > DescriptionMaxLength)
            return CategoryErrors.DescriptionTooLong;

        var existing = FindTranslation(cultureValue);
        if (existing is null)
            _translations.Add(new CategoryTranslation(Id, cultureValue, nameValue, descriptionValue));
        else
            existing.Update(nameValue, descriptionValue);

        return Result.Success();
    }

    /// <summary>Default ("uz") tarjimani o'chirib bo'lmaydi.</summary>
    public Result RemoveTranslation(string culture)
    {
        var cultureValue = Cultures.Normalize(culture);
        if (cultureValue is null)
            return CategoryErrors.CultureNotSupportedFor(culture ?? string.Empty);
        if (cultureValue == Cultures.Default)
            return CategoryErrors.CannotRemoveDefaultTranslation;

        var existing = FindTranslation(cultureValue);
        if (existing is null)
            return CategoryErrors.TranslationNotFoundFor(cultureValue);

        _translations.Remove(existing);
        return Result.Success();
    }

    /// <summary>
    /// Boshqa ota kategoriyaga ko'chirish (null — ildizga). Faqat o'z-o'ziga ota bo'lishni tekshiradi;
    /// sikl va chuqurlik tekshiruvi daraxtni bilishni talab qiladi, shuning uchun Application qatlamida.
    /// </summary>
    public Result MoveTo(Guid? newParentId, int order)
    {
        var parentId = NormalizeId(newParentId);
        if (parentId == Id)
            return CategoryErrors.CannotBeOwnParent;
        if (order < 0)
            return CategoryErrors.OrderInvalid;

        ParentId = parentId;
        Order = order;
        return Result.Success();
    }

    public Result ChangeSlug(string slug)
    {
        var value = slug?.Trim();
        if (!IsValidSlug(value))
            return CategoryErrors.SlugInvalid;

        Slug = value!;
        return Result.Success();
    }

    public void SetIcon(Guid? iconMediaId) => IconMediaId = NormalizeId(iconMediaId);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Tarjima: avval so'ralgan til, keyin fallback til, keyin default "uz", oxiri — birinchi mavjud tarjima.</summary>
    public CategoryTranslation? GetTranslation(string? culture, string? fallbackCulture = Cultures.Default) =>
        FindTranslation(Cultures.Normalize(culture))
        ?? FindTranslation(Cultures.Normalize(fallbackCulture))
        ?? FindTranslation(Cultures.Default)
        ?? _translations.FirstOrDefault();

    /// <summary>Nom (topilmasa slug qaytariladi).</summary>
    public string GetName(string? culture, string? fallbackCulture = Cultures.Default) =>
        GetTranslation(culture, fallbackCulture)?.Name ?? Slug;

    private CategoryTranslation? FindTranslation(string? culture) =>
        culture is null ? null : _translations.Find(t => t.Culture == culture);

    private static Guid? NormalizeId(Guid? id) => id == Guid.Empty ? null : id;
}
