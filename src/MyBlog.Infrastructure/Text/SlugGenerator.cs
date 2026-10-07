using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Text;

/// <summary>
/// URL slug. O'zbek kirill (rasmiy lotin alifbosiga mos) va rus harflarini lotinga o'giradi,
/// o'zbekcha tutuq belgilarini (o', g', ʻ, ʼ) olib tashlaydi.
/// Matnda o'zbekcha harf (ў, қ, ғ, ҳ) bo'lmasa va ruscha harf (ы, щ) bo'lsa — ruscha transliteratsiya (ж→zh, х→kh).
/// </summary>
internal sealed class SlugGenerator : ISlugGenerator
{
    private static readonly Dictionary<char, string> Common = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f",
        ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // O'zbek kirill
        ['ў'] = "o", ['қ'] = "q", ['ғ'] = "g", ['ҳ'] = "h",
        // Ukrain/qozoq kabi ba'zi qo'shimcha harflar
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ң'] = "ng", ['ү'] = "u", ['ұ'] = "u", ['ә'] = "a", ['ө'] = "o"
    };

    private static readonly Dictionary<char, string> Uzbek = new() { ['ж'] = "j", ['х'] = "x" };
    private static readonly Dictionary<char, string> Russian = new() { ['ж'] = "zh", ['х'] = "kh" };

    // O'zbekcha tutuq belgilari: o' g' va tutuq (sun'iy, ma'no).
    private static readonly HashSet<char> Apostrophes = ['\'', '`', 'ʻ', 'ʼ', '‘', '’', 'ʹ', '´'];

    public string Generate(string text, int maxLength = 120)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var slug = Slugify(text ?? string.Empty);

        if (slug.Length > maxLength)
            slug = Truncate(slug, maxLength);

        return slug.Length > 0 ? slug : RandomToken();
    }

    private static string Slugify(string text)
    {
        var lower = text.ToLowerInvariant();
        var specific = lower.Any(c => c is 'ў' or 'қ' or 'ғ' or 'ҳ') || !lower.Any(c => c is 'ы' or 'щ')
            ? Uzbek
            : Russian;

        var latin = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (Apostrophes.Contains(c))
                continue;
            if (specific.TryGetValue(c, out var mapped) || Common.TryGetValue(c, out mapped))
                latin.Append(mapped);
            else
                latin.Append(c);
        }

        // Diakritikalarni olib tashlash (é → e, ñ → n).
        var decomposed = latin.ToString().Normalize(NormalizationForm.FormD);

        var result = new StringBuilder(decomposed.Length);
        var pendingDash = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && result.Length > 0)
                    result.Append('-');
                pendingDash = false;
                result.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }

        return result.ToString();
    }

    private static string Truncate(string slug, int maxLength)
    {
        var cut = slug[..maxLength];

        // Keyingi belgi '-' bo'lsa, so'z butun kesilgan.
        if (slug[maxLength] == '-')
            return cut.TrimEnd('-');

        var lastDash = cut.LastIndexOf('-');
        return (lastDash > 0 ? cut[..lastDash] : cut).TrimEnd('-');
    }

    private static string RandomToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
}
