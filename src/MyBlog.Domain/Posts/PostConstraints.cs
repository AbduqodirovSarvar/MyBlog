namespace MyBlog.Domain.Posts;

public static class PostConstraints
{
    public const int TitleMaxLength = 200;
    public const int SlugMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int MaxTags = 10;

    // Kontent (EF'da text ustunlar; limitlar validator'lar uchun)
    public const int ContentFormatMaxLength = 32;
    public const int ContentHtmlMaxLength = 1_000_000;
    public const int ContentRawMaxLength = 2_000_000;
    public const int ContentPlainTextMaxLength = 500_000;
    public const int TableOfContentsMaxLength = 50_000;

    // SEO
    public const int MetaTitleMaxLength = 100;
    public const int MetaDescriptionMaxLength = 300;
    public const int CanonicalUrlMaxLength = 500;
}

/// <summary>Muharrir formatlari uchun tavsiya qilingan qiymatlar (format erkin satr, faqat uzunligi cheklangan).</summary>
public static class PostContentFormats
{
    public const string Html = "html";
    public const string TiptapJson = "tiptap-json";
    public const string CkEditor = "ckeditor";
}
