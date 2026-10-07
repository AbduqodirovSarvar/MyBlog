using MyBlog.Application.Features.Media.Abstractions;

namespace MyBlog.Infrastructure.Media;

/// <summary>JPEG, PNG, WebP va GIF fayllarini birinchi baytlari bo'yicha aniqlaydi.</summary>
internal sealed class MagicBytesSignatureValidator : IFileSignatureValidator
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";
    public const string Gif = "image/gif";

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87Magic = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89Magic = "GIF89a"u8.ToArray();
    private static readonly byte[] RiffMagic = "RIFF"u8.ToArray();
    private static readonly byte[] WebpMagic = "WEBP"u8.ToArray();

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpg"] = Jpeg,
        ["image/pjpeg"] = Jpeg,
        ["image/x-png"] = Png
    };

    public int HeaderLength => 12;

    public string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegMagic))
            return Jpeg;
        if (header.StartsWith(PngMagic))
            return Png;
        if (header.StartsWith(Gif87Magic) || header.StartsWith(Gif89Magic))
            return Gif;
        // RIFF <4 bayt hajm> WEBP
        if (header.Length >= 12 && header.StartsWith(RiffMagic) && header.Slice(8, 4).SequenceEqual(WebpMagic))
            return Webp;

        return null;
    }

    public bool Matches(ReadOnlySpan<byte> header, string declaredContentType) =>
        DetectContentType(header) is { } detected
        && string.Equals(detected, NormalizeContentType(declaredContentType), StringComparison.Ordinal);

    public string NormalizeContentType(string contentType)
    {
        // "image/jpeg; charset=..." kabi parametrlarni tashlaymiz.
        var value = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        return Aliases.TryGetValue(value, out var canonical) ? canonical : value;
    }
}
