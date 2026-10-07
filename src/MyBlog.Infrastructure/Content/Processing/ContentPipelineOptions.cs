using System.ComponentModel.DataAnnotations;
using MyBlog.Domain.Posts;

namespace MyBlog.Infrastructure.Content.Processing;

/// <summary>
/// Kontent pipeline sozlamalari ("Content" bo'limi; <see cref="ContentOptions"/> bilan bir bo'lim, alohida klass).
/// </summary>
internal sealed class ContentPipelineOptions
{
    public const string SectionName = "Content";

    public static readonly IReadOnlyList<string> DefaultRawDocumentFormats =
        ["tiptap-json", "ckeditor5", "editorjs-json", "quill-delta"];

    /// <summary>HTML + muharrir hujjati (Raw, JSON) yuboradigan formatlar. Bo'sh bo'lsa default ro'yxat.</summary>
    public string[] RawDocumentFormats { get; set; } = [];

    [Range(1, PostConstraints.ContentRawMaxLength)]
    public int MaxRawLength { get; set; } = PostConstraints.ContentRawMaxLength;

    [Range(1, 512)]
    public int MaxRawDepth { get; set; } = 256;

    [Range(50, 2000)]
    public int WordsPerMinute { get; set; } = 200;

    /// <summary>Bitta postdagi turli data-media-id'lar limiti.</summary>
    [Range(1, 10_000)]
    public int MaxMediaPerPost { get; set; } = 300;

    public IReadOnlyList<string> EffectiveRawDocumentFormats =>
        RawDocumentFormats.Length > 0 ? RawDocumentFormats : DefaultRawDocumentFormats;
}
