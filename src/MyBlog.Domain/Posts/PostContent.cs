using MyBlog.Domain.Common;
using static MyBlog.Domain.Posts.PostConstraints;

namespace MyBlog.Domain.Posts;

/// <summary>
/// Post kontenti (value object). Html — sanitize qilingan HTML, Raw — muharrirning o'z formatidagi hujjat (ixtiyoriy),
/// PlainText — qidiruv va o'qish vaqti uchun. O'zgartirilmaydi, faqat butunlay almashtiriladi.
/// </summary>
public sealed class PostContent
{
    private PostContent() { }

    private PostContent(string format, string html, string? raw, string plainText, string? tableOfContentsJson)
    {
        Format = format;
        Html = html;
        Raw = raw;
        PlainText = plainText;
        TableOfContentsJson = tableOfContentsJson;
    }

    public string Format { get; private set; } = null!;
    public string Html { get; private set; } = null!;
    public string? Raw { get; private set; }
    public string PlainText { get; private set; } = null!;
    public string? TableOfContentsJson { get; private set; }

    public static Result<PostContent> Create(string format, string html, string? raw, string plainText,
        string? tableOfContentsJson = null)
    {
        var formatValue = DomainRules.TrimToNull(format)?.ToLowerInvariant();
        if (formatValue is null)
            return PostErrors.ContentFormatRequired;
        if (formatValue.Length > ContentFormatMaxLength)
            return PostErrors.ContentFormatTooLong;

        var htmlValue = html ?? string.Empty;
        var plainValue = plainText?.Trim() ?? string.Empty;
        var rawValue = string.IsNullOrWhiteSpace(raw) ? null : raw;
        var tocValue = DomainRules.TrimToNull(tableOfContentsJson);

        if (htmlValue.Length > ContentHtmlMaxLength
            || rawValue?.Length > ContentRawMaxLength
            || plainValue.Length > ContentPlainTextMaxLength
            || tocValue?.Length > TableOfContentsMaxLength)
            return PostErrors.ContentTooLong;

        return new PostContent(formatValue, htmlValue, rawValue, plainValue, tocValue);
    }

    /// <summary>Bo'sh kontent (yangi qoralama uchun).</summary>
    public static PostContent Empty(string format = PostContentFormats.Html) =>
        new(format, string.Empty, null, string.Empty, null);

    /// <summary>EF owned instance'ni ikki owner'ga bog'lab bo'lmaydi (masalan Post va PostRevision), shuning uchun nusxa.</summary>
    public PostContent Copy() => new(Format, Html, Raw, PlainText, TableOfContentsJson);
}
