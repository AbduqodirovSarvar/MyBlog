using MyBlog.Domain.Categories;

namespace MyBlog.Domain.Tests.Categories;

public sealed class CategoryTests
{
    private static readonly Guid OwnerId = Guid.CreateVersion7();

    private static Category NewCategory() =>
        Category.Create(OwnerId, "dasturlash", null,
        [
            new CategoryTranslationData("uz", "Dasturlash"),
            new CategoryTranslationData("ru", "Программирование")
        ]).Value;

    [Fact]
    public void Create_WithDefaultTranslation_Succeeds()
    {
        var category = NewCategory();

        category.IsActive.ShouldBeTrue();
        category.Translations.Count.ShouldBe(2);
        category.Translations.ShouldAllBe(t => t.CategoryId == category.Id);
    }

    [Fact]
    public void Create_WithoutDefaultTranslation_Fails()
    {
        var result = Category.Create(OwnerId, "news", null, [new CategoryTranslationData("en", "News")]);

        result.Error.ShouldBe(CategoryErrors.DefaultTranslationRequired);
    }

    [Fact]
    public void Create_UnsupportedCulture_Fails()
    {
        var result = Category.Create(OwnerId, "news", null,
            [new CategoryTranslationData("uz", "Yangiliklar"), new CategoryTranslationData("de", "Nachrichten")]);

        result.Error.Code.ShouldBe(CategoryErrors.CultureNotSupported.Code);
    }

    [Fact]
    public void Create_DuplicateCulture_Fails()
    {
        var result = Category.Create(OwnerId, "news", null,
            [new CategoryTranslationData("uz", "A"), new CategoryTranslationData("UZ", "B")]);

        result.Error.Code.ShouldBe(CategoryErrors.DuplicateTranslation.Code);
    }

    [Fact]
    public void Create_InvalidSlug_Fails() =>
        Category.Create(OwnerId, "Not Valid", null, [new CategoryTranslationData("uz", "A")])
            .Error.ShouldBe(CategoryErrors.SlugInvalid);

    [Fact]
    public void RemoveTranslation_Default_Fails()
    {
        var category = NewCategory();

        category.RemoveTranslation("uz").Error.ShouldBe(CategoryErrors.CannotRemoveDefaultTranslation);
        category.Translations.Count.ShouldBe(2);
    }

    [Fact]
    public void RemoveTranslation_NonDefault_Succeeds()
    {
        var category = NewCategory();

        category.RemoveTranslation("ru").IsSuccess.ShouldBeTrue();

        category.Translations.ShouldHaveSingleItem().Culture.ShouldBe("uz");
    }

    [Fact]
    public void RemoveTranslation_Missing_Fails() =>
        NewCategory().RemoveTranslation("en").Error.Code.ShouldBe(CategoryErrors.TranslationNotFound.Code);

    [Fact]
    public void SetTranslation_UpdatesExisting()
    {
        var category = NewCategory();

        category.SetTranslation("uz", "Dasturlash 2", "Tavsif").IsSuccess.ShouldBeTrue();

        category.Translations.Count.ShouldBe(2);
        category.GetName("uz").ShouldBe("Dasturlash 2");
        category.GetTranslation("uz")!.Description.ShouldBe("Tavsif");
    }

    [Fact]
    public void SetTranslation_EmptyName_Fails() =>
        NewCategory().SetTranslation("en", "  ", null).Error.ShouldBe(CategoryErrors.NameRequired);

    [Theory]
    [InlineData("ru", "uz", "Программирование")]
    [InlineData("en", "uz", "Dasturlash")]
    [InlineData("en", "ru", "Программирование")]
    [InlineData(null, "uz", "Dasturlash")]
    [InlineData("uz-Cyrl", "en", "Dasturlash")]
    public void GetName_UsesFallback(string? culture, string fallback, string expected) =>
        NewCategory().GetName(culture, fallback).ShouldBe(expected);

    [Fact]
    public void MoveTo_Self_Fails()
    {
        var category = NewCategory();

        category.MoveTo(category.Id, 0).Error.ShouldBe(CategoryErrors.CannotBeOwnParent);
    }

    [Fact]
    public void MoveTo_OtherParent_Succeeds()
    {
        var category = NewCategory();
        var parentId = Guid.CreateVersion7();

        category.MoveTo(parentId, 3).IsSuccess.ShouldBeTrue();

        category.ParentId.ShouldBe(parentId);
        category.Order.ShouldBe(3);
    }

    [Fact]
    public void ActivateDeactivate_TogglesFlag()
    {
        var category = NewCategory();

        category.Deactivate();
        category.IsActive.ShouldBeFalse();

        category.Activate();
        category.IsActive.ShouldBeTrue();
    }
}
