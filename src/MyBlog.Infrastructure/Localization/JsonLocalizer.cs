using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Localization;

internal sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    [Required]
    public string DefaultCulture { get; set; } = "uz";

    [MinLength(1)]
    public string[] SupportedCultures { get; set; } = ["uz", "uz-Cyrl", "ru", "en"];
}

/// <summary>
/// Embedded JSON resurslaridan tarjima: <c>Localization/Resources/{modul}.{til}.json</c> (masalan auth.uz-Cyrl.json).
/// Bir tildagi barcha modul fayllari bitta lug'atga birlashtiriladi. Singleton, faqat o'qiladi.
/// Zanjir: so'ralgan til → default til → fallback → kalit.
/// </summary>
internal sealed class JsonLocalizer : ILocalizer
{
    private const string ResourcePrefix = "Localization/";

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _resources;
    private readonly string[] _supportedCultures;

    public JsonLocalizer(IOptions<LocalizationOptions> options)
        : this(options.Value, LoadEmbeddedResources(typeof(JsonLocalizer).Assembly))
    {
    }

    internal JsonLocalizer(LocalizationOptions options, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources)
    {
        _supportedCultures = options.SupportedCultures.Length > 0 ? options.SupportedCultures : [options.DefaultCulture];
        DefaultCulture = Match(options.DefaultCulture) ?? _supportedCultures[0];
        _resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(resources, StringComparer.OrdinalIgnoreCase);
    }

    public string CurrentCulture => NormalizeCulture(CultureInfo.CurrentUICulture.Name);

    public string DefaultCulture { get; }

    public IReadOnlyList<string> SupportedCultures => _supportedCultures;

    public string? Find(string key, string? culture = null)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        var normalized = culture is null ? CurrentCulture : NormalizeCulture(culture);
        return _resources.TryGetValue(normalized, out var dictionary) && dictionary.TryGetValue(key, out var value)
            ? value
            : null;
    }

    public string Get(string key, string? fallback = null, params object[] args)
    {
        var culture = CurrentCulture;
        var text = Find(key, culture) ?? Find(key, DefaultCulture) ?? fallback ?? key;

        if (args is not { Length: > 0 })
            return text;

        try
        {
            return string.Format(CultureInfo.GetCultureInfo(culture), text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>"uz-Cyrl-UZ" → "uz-Cyrl", "uz-Latn-UZ" → "uz", "ru-RU" → "ru"; noma'lum bo'lsa default.</summary>
    public string NormalizeCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return DefaultCulture;

        var candidate = culture.Trim().Replace('_', '-');
        while (candidate.Length > 0)
        {
            if (Match(candidate) is { } supported)
                return supported;

            var dash = candidate.LastIndexOf('-');
            if (dash < 0)
                break;
            candidate = candidate[..dash];
        }

        return DefaultCulture;
    }

    private string? Match(string culture) =>
        _supportedCultures.FirstOrDefault(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));

    internal static Dictionary<string, IReadOnlyDictionary<string, string>> LoadEmbeddedResources(Assembly assembly)
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            // "{modul}.{til}.json"
            var parts = name[ResourcePrefix.Length..].Split('.');
            if (parts.Length < 3)
                continue;
            var culture = parts[^2];

            using var stream = assembly.GetManifestResourceStream(name)!;
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Localization resource '{name}' is empty.");

            if (!merged.TryGetValue(culture, out var target))
                merged[culture] = target = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, value) in entries)
            {
                if (!target.TryAdd(key, value))
                    throw new InvalidOperationException($"Duplicate localization key '{key}' for culture '{culture}' (resource '{name}').");
            }
        }

        return merged.ToDictionary(
            p => p.Key,
            p => (IReadOnlyDictionary<string, string>)p.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
