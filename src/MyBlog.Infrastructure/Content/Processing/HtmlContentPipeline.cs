using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;

namespace MyBlog.Infrastructure.Content.Processing;

/// <summary>
/// Muharrirdan qat'i nazar HTML uchun umumiy qadamlar: uzunlik → sanitize → AngleSharp bilan parse →
/// data-media-id'larni tekshirish va img src/srcset'ni kanonik URL'ga qayta yozish → h2/h3 id'lari (TOC) →
/// plain text → o'qish vaqti.
/// </summary>
internal sealed partial class HtmlContentPipeline(
    IHtmlSanitizer sanitizer,
    IReadRepository<MediaFile> mediaRepository,
    IFileStorage storage,
    ISlugGenerator slugGenerator,
    IOptions<ContentOptions> contentOptions,
    IOptions<ContentPipelineOptions> pipelineOptions)
{
    private const string MediaIdAttribute = "data-media-id";
    private const int HeadingIdMaxLength = 80;

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "br", "dd", "div", "dl", "dt", "figcaption", "figure", "footer",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table",
        "tbody", "td", "tfoot", "th", "thead", "tr", "ul", "img"
    };

    private static readonly HashSet<string> IgnoredTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "template", "noscript"
    };

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    public async Task<Result<ProcessedContent>> ProcessAsync(string format, string? body, string? raw, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var html = body ?? string.Empty;

        // Sanitizer limitdan uzun HTML'da exception tashlaydi — avval tekshiramiz.
        var maxLength = Math.Min(contentOptions.Value.MaxHtmlLength, PostConstraints.ContentHtmlMaxLength);
        if (html.Length > maxLength)
            return PostErrors.ContentTooLong;

        var sanitized = sanitizer.Sanitize(html);

        var parser = new HtmlParser();
        using var document = parser.ParseDocument("<!DOCTYPE html><html><head></head><body></body></html>");
        var root = document.Body!;
        root.InnerHtml = sanitized;

        var media = await RewriteMediaAsync(root, ownerId, cancellationToken);
        if (media.IsFailure)
            return media.Error;

        var toc = AssignHeadingIds(root);
        var plainText = ExtractPlainText(root);
        var readingTime = ReadingTime(plainText, pipelineOptions.Value.WordsPerMinute);

        return new ProcessedContent(format, root.InnerHtml, raw, plainText, readingTime, media.Value, toc);
    }

    /// <summary>
    /// data-media-id'li elementlar: id muallifning media fayli bo'lishi shart; img src mijoz yuborgan qiymatdan qat'i nazar
    /// kanonik URL'ga almashtiriladi (eskirgan yoki begona URL kiritib bo'lmaydi), srcset variantlardan quriladi.
    /// </summary>
    private async Task<Result<IReadOnlyList<Guid>>> RewriteMediaAsync(IElement root, Guid ownerId, CancellationToken cancellationToken)
    {
        var elements = root.QuerySelectorAll($"[{MediaIdAttribute}]").ToList();
        if (elements.Count == 0)
            return Result.Success<IReadOnlyList<Guid>>([]);

        var references = new List<(IElement Element, Guid Id)>(elements.Count);
        foreach (var element in elements)
        {
            if (!Guid.TryParse(element.GetAttribute(MediaIdAttribute), out var id) || id == Guid.Empty)
                return PostErrors.InvalidMediaReference;
            references.Add((element, id));
        }

        var ids = references.Select(r => r.Id).Distinct().ToList();
        if (ids.Count > pipelineOptions.Value.MaxMediaPerPost)
            return PostErrors.ContentTooLong;

        var owned = (await mediaRepository.ListAsync(new MediaByIdsForOwnerSpec(ownerId, ids), cancellationToken))
            .ToDictionary(m => m.Id);
        if (owned.Count != ids.Count)
            return PostErrors.InvalidMediaReference;

        foreach (var (element, id) in references)
        {
            var file = owned[id];
            element.SetAttribute(MediaIdAttribute, id.ToString());

            var image = element.LocalName == "img" ? element : element.QuerySelector("img");
            if (image is null)
                continue;

            image.SetAttribute(MediaIdAttribute, id.ToString());
            image.SetAttribute("src", storage.GetPublicUrl(file.StorageKey));

            var srcset = BuildSrcSet(file);
            if (srcset is null)
                image.RemoveAttribute("srcset");
            else
                image.SetAttribute("srcset", srcset);

            image.SetAttribute("loading", "lazy");
            image.SetAttribute("decoding", "async");

            if (string.IsNullOrWhiteSpace(image.GetAttribute("alt")) && file.AltText is { } alt)
                image.SetAttribute("alt", alt);
        }

        return ids;
    }

    private string? BuildSrcSet(MediaFile file)
    {
        if (file.Variants.Count == 0 || file.Width is not { } originalWidth)
            return null;

        var candidates = file.Variants
            .Where(v => v.Width < originalWidth)
            .OrderBy(v => v.Width)
            .Select(v => $"{storage.GetPublicUrl(v.StorageKey)} {v.Width.ToString(CultureInfo.InvariantCulture)}w")
            .Append($"{storage.GetPublicUrl(file.StorageKey)} {originalWidth.ToString(CultureInfo.InvariantCulture)}w");

        return string.Join(", ", candidates);
    }

    /// <summary>h2/h3'ga sarlavha matnidan unikal id qo'yadi va mundarijani qaytaradi.</summary>
    private List<TocItem> AssignHeadingIds(IElement root)
    {
        var toc = new List<TocItem>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var heading in root.QuerySelectorAll("h2, h3"))
        {
            var text = Normalize(heading.TextContent);
            if (text.Length == 0)
            {
                heading.RemoveAttribute("id");
                continue;
            }

            var baseId = slugGenerator.Generate(text, HeadingIdMaxLength);
            var id = baseId;
            for (var i = 2; !used.Add(id); i++)
                id = $"{baseId}-{i.ToString(CultureInfo.InvariantCulture)}";

            heading.SetAttribute("id", id);
            toc.Add(new TocItem(heading.LocalName == "h2" ? 2 : 3, id, text));
        }

        return toc;
    }

    internal static string ExtractPlainText(IElement root)
    {
        var builder = new StringBuilder();
        AppendText(root, builder);
        return Normalize(builder.ToString());
    }

    private static void AppendText(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    builder.Append(text.Data);
                    break;
                case IElement element when IgnoredTextElements.Contains(element.LocalName):
                    break;
                case IElement element:
                    // Blok elementlar orasida so'zlar yopishib qolmasin ("<p>a</p><p>b</p>" → "a b").
                    var isBlock = BlockElements.Contains(element.LocalName);
                    if (isBlock)
                        builder.Append(' ');
                    AppendText(element, builder);
                    if (isBlock)
                        builder.Append(' ');
                    break;
            }
        }
    }

    internal static int ReadingTime(string plainText, int wordsPerMinute)
    {
        var words = string.IsNullOrWhiteSpace(plainText)
            ? 0
            : plainText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / (double)Math.Max(wordsPerMinute, 1)));
    }

    private static string Normalize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : WhitespaceRegex().Replace(text, " ").Trim();
}
