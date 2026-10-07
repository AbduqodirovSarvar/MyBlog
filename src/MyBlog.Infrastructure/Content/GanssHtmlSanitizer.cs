using System.ComponentModel.DataAnnotations;
using AngleSharp.Dom;
using Ganss.Xss;
using Microsoft.Extensions.Options;
using IAppHtmlSanitizer = MyBlog.Application.Abstractions.Services.IHtmlSanitizer;

namespace MyBlog.Infrastructure.Content;

internal sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>iframe faqat shu host'lardan (https) ruxsat etiladi.</summary>
    public string[] AllowedIframeHosts { get; set; } =
        ["youtube.com", "www.youtube.com", "youtube-nocookie.com", "www.youtube-nocookie.com", "player.vimeo.com"];

    [Range(1, int.MaxValue)]
    public int MaxHtmlLength { get; set; } = 500_000;
}

/// <summary>
/// Ganss.Xss.HtmlSanitizer asosida whitelist tozalash. Editor (rasm, figure, video embed) uchun kerakli
/// teg/atribut/CSS'lar ruxsat etilgan. Singleton: sozlamalar konstruktorda bir marta beriladi, Sanitize thread-safe.
/// </summary>
internal sealed class GanssHtmlSanitizer : IAppHtmlSanitizer
{
    private static readonly string[] ExtraTags = ["figure", "figcaption", "iframe"];

    private static readonly string[] ExtraAttributes =
    [
        "class", "style", "data-media-id", "data-align", "data-wrap", "data-width", "id",
        "loading", "width", "height", "allowfullscreen", "frameborder", "src"
    ];

    private static readonly string[] CssProperties =
    [
        "width", "height", "max-width", "float", "margin", "margin-left", "margin-right",
        "margin-top", "margin-bottom", "text-align"
    ];

    private readonly HtmlSanitizer _sanitizer;
    private readonly HashSet<string> _iframeHosts;
    private readonly int _maxLength;

    public GanssHtmlSanitizer(IOptions<ContentOptions> options)
    {
        _iframeHosts = new HashSet<string>(options.Value.AllowedIframeHosts, StringComparer.OrdinalIgnoreCase);
        _maxLength = options.Value.MaxHtmlLength;

        _sanitizer = new HtmlSanitizer();

        foreach (var tag in ExtraTags)
            _sanitizer.AllowedTags.Add(tag);
        foreach (var attribute in ExtraAttributes)
            _sanitizer.AllowedAttributes.Add(attribute);

        _sanitizer.AllowedCssProperties.Clear();
        foreach (var property in CssProperties)
            _sanitizer.AllowedCssProperties.Add(property);

        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in (string[])["http", "https", "mailto"])
            _sanitizer.AllowedSchemes.Add(scheme);

        _sanitizer.PostProcessDom += (_, e) => RemoveForeignIframes(e.Document);
    }

    /// <exception cref="ArgumentException">HTML <see cref="ContentOptions.MaxHtmlLength"/> dan uzun bo'lsa
    /// (validator'lar uzunlikni oldindan tekshirishi kerak).</exception>
    public string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        if (html.Length > _maxLength)
            throw new ArgumentException($"HTML content exceeds the maximum length of {_maxLength} characters.", nameof(html));

        return _sanitizer.Sanitize(html);
    }

    private void RemoveForeignIframes(IDocument document)
    {
        foreach (var iframe in document.QuerySelectorAll("iframe").ToList())
        {
            if (!IsAllowedIframeSource(iframe.GetAttribute("src")))
                iframe.Remove();
        }
    }

    internal bool IsAllowedIframeSource(string? src) =>
        Uri.TryCreate(src, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && _iframeHosts.Contains(uri.Host);
}
