using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Options;
using MyBlog.Infrastructure.Localization;

namespace MyBlog.Infrastructure.Tests.Localization;

public sealed class JsonLocalizerTests
{
    private static readonly LocalizationOptions Options = new();

    private static JsonLocalizer CreateLocalizer() => new(Options, new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["uz"] = new Dictionary<string, string>
        {
            ["Test.Both"] = "O'zbekcha",
            ["Test.OnlyDefault"] = "Faqat o'zbekcha",
            ["Test.Format"] = "{0} ta post"
        },
        ["ru"] = new Dictionary<string, string>
        {
            ["Test.Both"] = "По-русски",
            ["Test.Format"] = "{0} постов"
        }
    });

    private static T WithCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Returns_translation_for_current_culture()
    {
        var localizer = CreateLocalizer();

        WithCulture("ru", () => localizer.Get("Test.Both")).ShouldBe("По-русски");
        WithCulture("uz", () => localizer.Get("Test.Both")).ShouldBe("O'zbekcha");
    }

    [Fact]
    public void Falls_back_to_default_culture_then_fallback_then_key()
    {
        var localizer = CreateLocalizer();

        WithCulture("ru", () => localizer.Get("Test.OnlyDefault")).ShouldBe("Faqat o'zbekcha");
        WithCulture("ru", () => localizer.Get("Test.Missing", "Fallback text")).ShouldBe("Fallback text");
        WithCulture("ru", () => localizer.Get("Test.Missing")).ShouldBe("Test.Missing");
    }

    [Fact]
    public void Find_returns_null_when_missing_in_requested_culture()
    {
        var localizer = CreateLocalizer();

        localizer.Find("Test.OnlyDefault", "ru").ShouldBeNull();
        localizer.Find("Test.OnlyDefault", "uz").ShouldBe("Faqat o'zbekcha");
        localizer.Find("Test.Both", "ru-RU").ShouldBe("По-русски");
    }

    [Fact]
    public void Formats_arguments()
    {
        var localizer = CreateLocalizer();

        WithCulture("ru", () => localizer.Get("Test.Format", null, 5)).ShouldBe("5 постов");
        WithCulture("en", () => localizer.Get("Test.Format", null, 3)).ShouldBe("3 ta post");
    }

    [Theory]
    [InlineData(null, "uz")]
    [InlineData("", "uz")]
    [InlineData("uz", "uz")]
    [InlineData("UZ", "uz")]
    [InlineData("uz-Latn-UZ", "uz")]
    [InlineData("uz-UZ", "uz")]
    [InlineData("uz-Cyrl", "uz-Cyrl")]
    [InlineData("uz-cyrl-UZ", "uz-Cyrl")]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "uz")]
    [InlineData("uz_Cyrl", "uz-Cyrl")]
    public void Normalizes_cultures(string? input, string expected) =>
        CreateLocalizer().NormalizeCulture(input).ShouldBe(expected);

    [Fact]
    public void Current_culture_is_normalized()
    {
        var localizer = CreateLocalizer();

        WithCulture("uz-Cyrl-UZ", () => localizer.CurrentCulture).ShouldBe("uz-Cyrl");
        WithCulture("fr-FR", () => localizer.CurrentCulture).ShouldBe("uz");
    }

    [Fact]
    public void Embedded_common_resources_exist_for_all_supported_cultures_with_same_keys()
    {
        var resources = JsonLocalizer.LoadEmbeddedResources(typeof(JsonLocalizer).Assembly);

        resources.Keys.ShouldBe(Options.SupportedCultures, ignoreOrder: true);

        var referenceKeys = resources["uz"].Keys.Order().ToList();
        referenceKeys.ShouldContain("General.Validation");
        referenceKeys.ShouldContain("General.ConcurrencyConflict");
        foreach (var culture in Options.SupportedCultures)
            resources[culture].Keys.Order().ToList().ShouldBe(referenceKeys, $"culture {culture}");
    }

    [Fact]
    public void Embedded_resources_are_used_by_default_constructor()
    {
        var localizer = new JsonLocalizer(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));

        localizer.Find("General.NotFound", "uz-Cyrl").ShouldBe("Сўралган маълумот топилмади.");
        localizer.Find("General.NotFound", "ru").ShouldBe("Запрашиваемые данные не найдены.");
    }

    [Theory]
    [InlineData("uz")]
    [InlineData("uz-Cyrl")]
    [InlineData("ru")]
    [InlineData("en")]
    public void Supported_cultures_are_known_to_the_runtime(string culture) =>
        Should.NotThrow(() => CultureInfo.GetCultureInfo(culture));
}

public sealed class UzbekAwareLanguageManagerTests
{
    [Theory]
    [InlineData("uz", "uz-Latn-UZ")]
    [InlineData("uz-Latn", "uz-Latn-UZ")]
    [InlineData("uz-Cyrl", "uz-Cyrl-UZ")]
    [InlineData("uz-Cyrl-UZ", "uz-Cyrl-UZ")]
    [InlineData("ru", "ru")]
    public void Maps_our_cultures_to_fluent_validation_cultures(string input, string expected) =>
        UzbekAwareLanguageManager.Map(CultureInfo.GetCultureInfo(input)).Name.ShouldBe(expected);

    [Fact]
    public void Returns_uzbek_messages_for_short_culture_names()
    {
        var manager = new UzbekAwareLanguageManager();
        var english = manager.GetString("NotEmptyValidator", CultureInfo.GetCultureInfo("en"));

        var latin = manager.GetString("NotEmptyValidator", CultureInfo.GetCultureInfo("uz"));
        var cyrillic = manager.GetString("NotEmptyValidator", CultureInfo.GetCultureInfo("uz-Cyrl"));

        latin.ShouldNotBe(english);
        cyrillic.ShouldNotBe(english);
        cyrillic.ShouldNotBe(latin);
        cyrillic.Any(c => c is >= 'Ѐ' and <= 'ӿ').ShouldBeTrue();
    }

    [Fact]
    public void Validator_messages_are_localized_through_global_options()
    {
        var previous = ValidatorOptions.Global.LanguageManager;
        ValidatorOptions.Global.LanguageManager = new UzbekAwareLanguageManager();
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            var validator = new InlineValidator<string>();
            validator.RuleFor(x => x).NotEmpty();

            validator.Validate(string.Empty).Errors.Single().ErrorMessage.ShouldContain("'");
            validator.Validate(string.Empty).Errors.Single().ErrorMessage.Any(c => c is >= 'Ѐ' and <= 'ӿ').ShouldBeTrue();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            ValidatorOptions.Global.LanguageManager = previous;
        }
    }
}
