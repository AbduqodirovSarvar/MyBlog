using System.Globalization;
using FluentValidation.Resources;

namespace MyBlog.Infrastructure.Localization;

/// <summary>
/// FluentValidation o'zbek tarjimalarini "uz-Latn-UZ" va "uz-Cyrl-UZ" nomlari bilan saqlaydi,
/// bizning tillar esa "uz" va "uz-Cyrl". Shu farqni moslaydi.
/// </summary>
internal sealed class UzbekAwareLanguageManager : LanguageManager
{
    private static readonly CultureInfo UzbekLatin = CultureInfo.GetCultureInfo("uz-Latn-UZ");
    private static readonly CultureInfo UzbekCyrillic = CultureInfo.GetCultureInfo("uz-Cyrl-UZ");

    public override string GetString(string key, CultureInfo? culture = null)
    {
        culture ??= Culture ?? CultureInfo.CurrentUICulture;
        return base.GetString(key, Map(culture));
    }

    internal static CultureInfo Map(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.StartsWith("uz-Cyrl", StringComparison.OrdinalIgnoreCase))
            return UzbekCyrillic;
        if (name.Equals("uz", StringComparison.OrdinalIgnoreCase) || name.StartsWith("uz-", StringComparison.OrdinalIgnoreCase))
            return UzbekLatin;
        return culture;
    }
}
