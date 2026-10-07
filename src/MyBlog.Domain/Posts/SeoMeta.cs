using MyBlog.Domain.Common;
using static MyBlog.Domain.Posts.PostConstraints;

namespace MyBlog.Domain.Posts;

/// <summary>SEO meta ma'lumotlari (value object). Hamma maydonlar ixtiyoriy.</summary>
public sealed class SeoMeta
{
    private SeoMeta() { }

    private SeoMeta(string? metaTitle, string? metaDescription, Guid? ogImageMediaId, string? canonicalUrl)
    {
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
        OgImageMediaId = ogImageMediaId;
        CanonicalUrl = canonicalUrl;
    }

    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }
    public Guid? OgImageMediaId { get; private set; }
    public string? CanonicalUrl { get; private set; }

    /// <summary>Har safar yangi instance (EF owned instance'lar ulashilmasligi kerak).</summary>
    public static SeoMeta Empty => new();

    public static Result<SeoMeta> Create(string? metaTitle, string? metaDescription, Guid? ogImageMediaId,
        string? canonicalUrl)
    {
        var titleValue = DomainRules.TrimToNull(metaTitle);
        var descriptionValue = DomainRules.TrimToNull(metaDescription);
        var urlValue = DomainRules.TrimToNull(canonicalUrl);

        if (titleValue?.Length > MetaTitleMaxLength)
            return PostErrors.MetaTitleTooLong;
        if (descriptionValue?.Length > MetaDescriptionMaxLength)
            return PostErrors.MetaDescriptionTooLong;
        if (urlValue is not null && (urlValue.Length > CanonicalUrlMaxLength || !DomainRules.IsValidHttpUrl(urlValue)))
            return PostErrors.CanonicalUrlInvalid;

        return new SeoMeta(titleValue, descriptionValue, ogImageMediaId == Guid.Empty ? null : ogImageMediaId, urlValue);
    }

    public SeoMeta Copy() => new(MetaTitle, MetaDescription, OgImageMediaId, CanonicalUrl);
}
