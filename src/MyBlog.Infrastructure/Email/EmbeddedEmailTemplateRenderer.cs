using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Email;

/// <summary>
/// Embedded shablonlar: <c>Email/Templates/{til}/{nom}.html</c>. Shablonda <c>&lt;title&gt;</c> — mavzu,
/// qolgani — body fragmenti; natija <c>_layout.html</c> ichidagi <c>{{body}}</c> o'rniga qo'yiladi.
/// Til topilmasa default tildagi shablon ishlatiladi. Singleton.
/// </summary>
internal sealed class EmbeddedEmailTemplateRenderer : IEmailTemplateRenderer
{
    internal const string ResourcePrefix = "Email/Templates/";
    internal const string LayoutName = "_layout";

    private readonly ILocalizer _localizer;
    private readonly Dictionary<string, string> _templates;

    public EmbeddedEmailTemplateRenderer(ILocalizer localizer)
    {
        _localizer = localizer;
        _templates = LoadTemplates(typeof(EmbeddedEmailTemplateRenderer).Assembly);
    }

    public Task<RenderedEmail> RenderAsync(string templateName, string culture, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentNullException.ThrowIfNull(values);

        var normalized = _localizer.NormalizeCulture(culture);
        var (template, templateCulture) = Find(templateName, normalized)
            ?? throw new InvalidOperationException($"Email template '{templateName}' was not found for culture '{normalized}'.");
        var layout = Find(LayoutName, templateCulture)?.Content;

        return Task.FromResult(EmailTemplateEngine.Render(template, layout, values, templateCulture));
    }

    private (string Content, string Culture)? Find(string name, string culture)
    {
        foreach (var candidate in (string[])[culture, _localizer.DefaultCulture])
        {
            if (_templates.TryGetValue($"{candidate}/{name}", out var content))
                return (content, candidate);
        }

        return null;
    }

    private static Dictionary<string, string> LoadTemplates(Assembly assembly)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var name = resource.Replace('\\', '/');
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);

            // "{til}/{nom}"
            templates[name[ResourcePrefix.Length..^".html".Length]] = reader.ReadToEnd();
        }

        return templates;
    }
}

/// <summary>Shablonni to'ldirish (sof funksiya, test qilinadi).</summary>
internal static partial class EmailTemplateEngine
{
    public static RenderedEmail Render(string template, string? layout, IReadOnlyDictionary<string, string> values, string culture)
    {
        var filled = ReplacePlaceholders(template, values);

        var titleMatch = TitleRegex().Match(filled);
        var subject = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim() : string.Empty;
        var body = (titleMatch.Success ? filled.Remove(titleMatch.Index, titleMatch.Length) : filled).Trim();

        var html = body;
        if (layout is not null)
        {
            var layoutValues = new Dictionary<string, string>(values) { ["title"] = subject, ["lang"] = culture };
            var filledLayout = ReplacePlaceholders(layout.Replace("{{body}}", "\u0000BODY\u0000", StringComparison.Ordinal), layoutValues);
            html = filledLayout.Replace("\u0000BODY\u0000", body, StringComparison.Ordinal);
        }

        return new RenderedEmail(subject, html, ToPlainText(body));
    }

    /// <summary>{{key}} → HTML-encode qilingan qiymat; noma'lum kalitlar bo'sh qatorga almashadi.</summary>
    public static string ReplacePlaceholders(string text, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex().Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var value) ? WebUtility.HtmlEncode(value) : string.Empty);

    public static string ToPlainText(string html)
    {
        var text = StyleOrScriptRegex().Replace(html, string.Empty);
        text = LinkRegex().Replace(text, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            var label = TagRegex().Replace(m.Groups["text"].Value, string.Empty).Trim();
            return label.Length == 0 || WebUtility.HtmlDecode(label) == href ? href : $"{label} ({href})";
        });
        text = LineBreakRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(l => SpacesRegex().Replace(l, " ").Trim());
        return MultipleBlankLinesRegex().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<(style|script)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleOrScriptRegex();

    [GeneratedRegex("""<a\s[^>]*href\s*=\s*["'](?<href>[^"']*)["'][^>]*>(?<text>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|h[1-6]|li|tr|table)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLinesRegex();
}
