using System.Reflection;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Localization;

namespace MyBlog.Infrastructure.Tests.Localization;

/// <summary>Profile/Category/Tag domain xato kodlarining barchasi 4 tilda tarjima qilingan.</summary>
public sealed class ProfileModuleLocalizationTests
{
    private static readonly string[] Cultures = ["uz", "uz-Cyrl", "ru", "en"];

    public static TheoryData<string> ErrorCodes()
    {
        var codes = new[] { typeof(UserProfileErrors), typeof(CategoryErrors), typeof(TagErrors) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => ((Error)f.GetValue(null)!).Code)
            .Concat(["Profile.BirthDateInFuture", "Profile.CultureNotSupported", "Category.DuplicateCulture", "Category.SlugFormatInvalid"])
            .Distinct()
            .Order();

        return new TheoryData<string>(codes);
    }

    [Theory]
    [MemberData(nameof(ErrorCodes))]
    public void Error_code_is_translated_in_every_culture(string code)
    {
        var resources = JsonLocalizer.LoadEmbeddedResources(typeof(JsonLocalizer).Assembly);

        foreach (var culture in Cultures)
        {
            resources.ShouldContainKey(culture);
            resources[culture].ShouldContainKey(code, $"'{code}' is missing for '{culture}'");
            resources[culture][code].ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Uzbek_cyrillic_resources_are_written_in_cyrillic()
    {
        var resources = JsonLocalizer.LoadEmbeddedResources(typeof(JsonLocalizer).Assembly)["uz-Cyrl"];

        resources["Category.NotFound"].ShouldBe("Категория топилмади.");
        resources["UserProfile.SkillLevelOutOfRange"].ShouldContain("Кўникма");
    }
}
