using Microsoft.Extensions.Logging;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using SkiaSharp;

namespace MyBlog.Infrastructure.Media;

/// <summary>
/// SkiaSharp asosida: EXIF orientatsiyasini qo'llaydi, asl rasmni qayta kodlab metadata'ni (GPS va h.k.) olib tashlaydi,
/// variantlarni WebP'da yaratadi. GIF va animatsiyali rasmlar o'zgartirilmaydi (variantsiz). Kattalashtirilmaydi.
/// Singleton, holatsiz.
/// </summary>
internal sealed class SkiaImageProcessor(ILogger<SkiaImageProcessor> logger) : IImageProcessor
{
    private const string WebpContentType = "image/webp";

    public Result<ImageProbe> Probe(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            using var data = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(data);
            if (codec is null || ContentTypeOf(codec.EncodedFormat) is not { } contentType)
                return MediaErrors.InvalidImage;

            var (width, height) = OrientedSize(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin);
            return new ImageProbe(contentType, width, height, Math.Max(codec.FrameCount, 1));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "Image probe failed");
            return MediaErrors.InvalidImage;
        }
    }

    public Result<ProcessedImage> Process(byte[] content, ImageProcessingRequest request)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var data = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(data);
            if (codec is null || ContentTypeOf(codec.EncodedFormat) is not { } contentType)
                return MediaErrors.InvalidImage;
            if (!string.Equals(contentType, request.ContentType, StringComparison.OrdinalIgnoreCase))
                return MediaErrors.FileContentMismatch;

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0)
                return MediaErrors.InvalidImage;
            if ((long)width * height > request.MaxPixels)
                return MediaErrors.ImageTooLarge;

            // GIF va animatsiya: kadrlarni buzmaslik uchun asl baytlar, variantsiz.
            if (contentType == MagicBytesSignatureValidator.Gif || codec.FrameCount > 1)
                return new ProcessedImage(new EncodedImage(content, contentType, ExtensionOf(contentType), width, height), []);

            var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
            var decodeInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType, SKColorSpace.CreateSrgb());

            using var decoded = SKBitmap.Decode(codec, decodeInfo);
            if (decoded is null)
                return MediaErrors.InvalidImage;

            using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);
            var source = oriented ?? decoded;

            var original = Encode(source, FormatOf(contentType), request.OriginalQuality, contentType);
            if (original is null)
                return MediaErrors.InvalidImage;

            var variants = new List<ProcessedImageVariant>(request.Variants.Count);
            foreach (var spec in request.Variants.Where(v => v.MaxWidth > 0).OrderBy(v => v.MaxWidth))
            {
                // Kattalashtirmaymiz: rasm variant kengligidan kichik yoki teng bo'lsa variant yaratilmaydi.
                if (source.Width <= spec.MaxWidth)
                    continue;

                var targetHeight = Math.Max(1, (int)Math.Round(source.Height * (double)spec.MaxWidth / source.Width));
                using var resized = Resize(source, spec.MaxWidth, targetHeight);
                if (resized is null)
                    return MediaErrors.InvalidImage;

                var encoded = Encode(resized, SKEncodedImageFormat.Webp, request.Quality, WebpContentType);
                if (encoded is null)
                    return MediaErrors.InvalidImage;

                variants.Add(new ProcessedImageVariant(spec.Name, encoded));
            }

            return new ProcessedImage(original, variants);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Image processing failed");
            return MediaErrors.InvalidImage;
        }
    }

    internal static string? ContentTypeOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => MagicBytesSignatureValidator.Jpeg,
        SKEncodedImageFormat.Png => MagicBytesSignatureValidator.Png,
        SKEncodedImageFormat.Webp => MagicBytesSignatureValidator.Webp,
        SKEncodedImageFormat.Gif => MagicBytesSignatureValidator.Gif,
        _ => null
    };

    private static SKEncodedImageFormat FormatOf(string contentType) => contentType switch
    {
        MagicBytesSignatureValidator.Png => SKEncodedImageFormat.Png,
        MagicBytesSignatureValidator.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Jpeg
    };

    private static string ExtensionOf(string contentType) => contentType switch
    {
        MagicBytesSignatureValidator.Png => "png",
        MagicBytesSignatureValidator.Webp => "webp",
        MagicBytesSignatureValidator.Gif => "gif",
        _ => "jpg"
    };

    private static EncodedImage? Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality, string contentType)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, Math.Clamp(quality, 1, 100));
        return encoded is null
            ? null
            : new EncodedImage(encoded.ToArray(), contentType, ExtensionOf(contentType), bitmap.Width, bitmap.Height);
    }

    /// <summary>Katta kichraytirishda aliasing bo'lmasligi uchun avval ikki baravardan bosqichma-bosqich, oxirida Mitchell.</summary>
    private static SKBitmap? Resize(SKBitmap source, int width, int height)
    {
        var current = source;
        try
        {
            while (current.Width / 2 >= width * 2)
            {
                var halved = current.Resize(
                    new SKImageInfo(current.Width / 2, Math.Max(1, current.Height / 2), current.ColorType, current.AlphaType, current.ColorSpace),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                if (halved is null)
                    return null;
                if (!ReferenceEquals(current, source))
                    current.Dispose();
                current = halved;
            }

            return current.Resize(
                new SKImageInfo(width, height, current.ColorType, current.AlphaType, current.ColorSpace),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        finally
        {
            if (!ReferenceEquals(current, source))
                current.Dispose();
        }
    }

    private static (int Width, int Height) OrientedSize(int width, int height, SKEncodedOrigin origin) =>
        SwapsAxes(origin) ? (height, width) : (width, height);

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>EXIF orientatsiyasini piksellarga qo'llaydi; o'zgartirish kerak bo'lmasa null.</summary>
    internal static SKBitmap? ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft || !Enum.IsDefined(origin))
            return null;

        var (width, height) = OrientedSize(source.Width, source.Height, origin);
        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(result);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // gorizontal aks
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180°
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // vertikal aks
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transpose
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // 90° soat yo'nalishida
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.Translate(width, height);
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(270);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° soat yo'nalishiga teskari
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }

        // Burish/aks ettirish piksellarni 1:1 ko'chiradi — interpolyatsiya kerak emas.
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Flush();
        return result;
    }
}
