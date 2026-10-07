using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Domain.Media;
using MyBlog.Infrastructure.Media;
using SkiaSharp;

namespace MyBlog.Infrastructure.Tests.Media;

public sealed class ImageProcessingTests
{
    private static readonly ImageVariantSpec[] Variants =
        [new("thumb", 320), new("medium", 960), new("large", 1920)];

    // 1x1 GIF89a
    private static readonly byte[] TinyGif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    private readonly SkiaImageProcessor _processor = new(NullLogger<SkiaImageProcessor>.Instance);
    private readonly MagicBytesSignatureValidator _signatures = new();

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SkyBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static ImageProcessingRequest Request(string contentType, long maxPixels = 40_000_000) =>
        new(contentType, Variants, 82, 90, maxPixels);

    [Fact]
    public void Large_png_produces_webp_variants_with_proportional_sizes()
    {
        var result = _processor.Process(CreateImage(2400, 1200, SKEncodedImageFormat.Png), Request("image/png"));

        result.IsSuccess.ShouldBeTrue();
        var image = result.Value;
        image.Original.ContentType.ShouldBe("image/png");
        image.Original.Extension.ShouldBe("png");
        (image.Original.Width, image.Original.Height).ShouldBe((2400, 1200));

        image.Variants.Select(v => (v.Name, v.Image.Width, v.Image.Height))
            .ShouldBe([("thumb", 320, 160), ("medium", 960, 480), ("large", 1920, 960)]);

        foreach (var variant in image.Variants)
        {
            variant.Image.ContentType.ShouldBe("image/webp");
            variant.Image.Extension.ShouldBe("webp");
            _signatures.DetectContentType(variant.Image.Content).ShouldBe("image/webp");
        }
    }

    [Fact]
    public void Small_images_are_not_upscaled()
    {
        var result = _processor.Process(CreateImage(500, 250, SKEncodedImageFormat.Jpeg), Request("image/jpeg"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Original.Width.ShouldBe(500);
        result.Value.Original.ContentType.ShouldBe("image/jpeg");
        result.Value.Variants.ShouldHaveSingleItem().Name.ShouldBe("thumb");
        result.Value.Variants[0].Image.Width.ShouldBe(320);

        var tiny = _processor.Process(CreateImage(100, 50, SKEncodedImageFormat.Png), Request("image/png"));
        tiny.Value.Variants.ShouldBeEmpty();
    }

    [Fact]
    public void Gif_is_kept_as_original_without_variants()
    {
        var result = _processor.Process(TinyGif, Request("image/gif"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Original.Content.ShouldBe(TinyGif);
        result.Value.Original.Extension.ShouldBe("gif");
        result.Value.Variants.ShouldBeEmpty();
    }

    [Fact]
    public void Declared_type_must_match_actual_image_format()
    {
        var jpeg = CreateImage(50, 50, SKEncodedImageFormat.Jpeg);

        _processor.Process(jpeg, Request("image/png")).Error.ShouldBe(MediaErrors.FileContentMismatch);
        _signatures.Matches(jpeg, "image/png").ShouldBeFalse();
        _signatures.Matches(jpeg, "image/jpg").ShouldBeTrue();
    }

    [Fact]
    public void Garbage_and_oversized_images_are_rejected()
    {
        _processor.Process([0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02], Request("image/jpeg")).Error.ShouldBe(MediaErrors.InvalidImage);
        _processor.Process(CreateImage(100, 100, SKEncodedImageFormat.Png), Request("image/png", maxPixels: 5000))
            .Error.ShouldBe(MediaErrors.ImageTooLarge);
    }

    [Fact]
    public void Probe_reports_dimensions_and_format()
    {
        var probe = _processor.Probe(CreateImage(64, 32, SKEncodedImageFormat.Webp));

        probe.Value.ShouldBe(new ImageProbe("image/webp", 64, 32, 1));
    }

    [Theory]
    [InlineData(SKEncodedOrigin.RightTop, true)] // 90° soat yo'nalishida: chap (qizil) tepaga
    [InlineData(SKEncodedOrigin.LeftBottom, false)] // 90° teskari: o'ng (ko'k) tepaga
    public void Exif_orientation_rotates_pixels(SKEncodedOrigin origin, bool redOnTop)
    {
        using var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);

        using var rotated = SkiaImageProcessor.ApplyOrigin(source, origin);

        rotated.ShouldNotBeNull();
        (rotated.Width, rotated.Height).ShouldBe((1, 2));
        rotated.GetPixel(0, 0).ShouldBe(redOnTop ? SKColors.Red : SKColors.Blue);
        rotated.GetPixel(0, 1).ShouldBe(redOnTop ? SKColors.Blue : SKColors.Red);
        SkiaImageProcessor.ApplyOrigin(source, SKEncodedOrigin.TopLeft).ShouldBeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, null)] // WAV
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, null)] // EXE
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)] // <svg
    public void Magic_bytes_are_detected(byte[] header, string? expected) =>
        _signatures.DetectContentType(header).ShouldBe(expected);
}
