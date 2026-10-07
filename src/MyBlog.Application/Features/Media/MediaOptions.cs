using System.ComponentModel.DataAnnotations;

namespace MyBlog.Application.Features.Media;

/// <summary>"Media" bo'limi. Infrastructure (MediaModule) bog'laydi.</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public static readonly IReadOnlyList<string> DefaultContentTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Bo'sh bo'lsa <see cref="DefaultContentTypes"/> ishlatiladi (binder massivga qo'shib yuboradi, shuning uchun default bo'sh).</summary>
    public string[] AllowedContentTypes { get; set; } = [];

    /// <summary>Variant nomi → maksimal kenglik (px). Kichik rasmlar kattalashtirilmaydi.</summary>
    public Dictionary<string, int> Variants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>WebP variantlar sifati.</summary>
    [Range(1, 100)]
    public int Quality { get; set; } = 82;

    /// <summary>Asl rasmni qayta kodlash sifati (metadata/EXIF olib tashlanadi).</summary>
    [Range(1, 100)]
    public int OriginalQuality { get; set; } = 90;

    /// <summary>Decode qilishdan oldin tekshiriladigan piksel limiti (decompression bomb'dan himoya).</summary>
    [Range(1, long.MaxValue)]
    public long MaxPixels { get; set; } = 40_000_000;

    [Range(1, int.MaxValue)]
    public int OrphanRetentionHours { get; set; } = 24;

    [Range(1, 10_000)]
    public int OrphanCleanupBatchSize { get; set; } = 200;

    public IReadOnlyList<string> EffectiveContentTypes =>
        AllowedContentTypes.Length > 0 ? AllowedContentTypes : DefaultContentTypes;
}
