using System.Text.RegularExpressions;

namespace MyBlog.Domain.Common;

/// <summary>Bir nechta modulda takrorlanadigan oddiy tekshiruvlar (validator'lar ham ishlatishi mumkin).</summary>
public static partial class DomainRules
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    /// <summary>Slug: kichik lotin harflari, raqamlar va bitta "-" bilan ajratilgan qismlar.</summary>
    public static bool IsValidSlug(string? slug) => !string.IsNullOrEmpty(slug) && SlugRegex().IsMatch(slug);

    public static bool IsValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);

    /// <summary>Bo'sh yoki faqat probeldan iborat bo'lsa null, aks holda trim qilingan qiymat.</summary>
    public static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
