using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Media.Abstractions;

/// <summary>Variant: nomi va maksimal kengligi.</summary>
public sealed record ImageVariantSpec(string Name, int MaxWidth);

public sealed record ImageProcessingRequest(
    string ContentType,
    IReadOnlyList<ImageVariantSpec> Variants,
    int Quality,
    int OriginalQuality,
    long MaxPixels);

/// <summary>Kodlangan rasm (asl yoki variant).</summary>
public sealed record EncodedImage(byte[] Content, string ContentType, string Extension, int Width, int Height);

public sealed record ProcessedImageVariant(string Name, EncodedImage Image);

/// <param name="Original">EXIF orientatsiyasi qo'llangan va metadata'si olib tashlangan asl rasm (GIF/animatsiya — o'zgarmagan).</param>
public sealed record ProcessedImage(EncodedImage Original, IReadOnlyList<ProcessedImageVariant> Variants);

public sealed record ImageProbe(string ContentType, int Width, int Height, int FrameCount);

/// <summary>
/// Rasmni tekshirish va qayta ishlash. JPEG/PNG/WebP variantlari WebP'da; GIF va animatsiyali rasmlar asl holida
/// saqlanadi (animatsiya buzilmasin). Kattalashtirish (upscale) qilinmaydi.
/// </summary>
public interface IImageProcessor
{
    /// <summary>O'lcham va formatni aniqlaydi (to'liq decode qilmasdan).</summary>
    Result<ImageProbe> Probe(byte[] content);

    Result<ProcessedImage> Process(byte[] content, ImageProcessingRequest request);
}
