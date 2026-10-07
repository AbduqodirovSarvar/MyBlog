namespace MyBlog.Domain.Common;

/// <summary>Qo'llab-quvvatlanadigan UI tillari. "uz" — default va majburiy til.</summary>
public static class Cultures
{
    public const string Uzbek = "uz";
    public const string UzbekCyrillic = "uz-Cyrl";
    public const string Russian = "ru";
    public const string English = "en";

    public const string Default = Uzbek;

    public const int MaxLength = 10;

    public static IReadOnlyList<string> Supported { get; } = [Uzbek, UzbekCyrillic, Russian, English];

    public static bool IsSupported(string? culture) => Normalize(culture) is not null;

    /// <summary>Katta-kichik harfdan qat'i nazar kanonik ko'rinishni qaytaradi ("UZ-cyrl" → "uz-Cyrl"); topilmasa null.</summary>
    public static string? Normalize(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return null;

        var trimmed = culture.Trim();
        foreach (var supported in Supported)
        {
            if (string.Equals(supported, trimmed, StringComparison.OrdinalIgnoreCase))
                return supported;
        }

        return null;
    }
}
