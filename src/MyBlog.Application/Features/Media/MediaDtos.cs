using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Media;

public sealed record MediaVariantDto(string Url, int Width, int Height);

public sealed record MediaDto(
    Guid Id,
    string Url,
    int? Width,
    int? Height,
    string ContentType,
    long SizeBytes,
    string OriginalFileName,
    string? AltText,
    string? Caption,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, MediaVariantDto> Variants);

public static class MediaMapping
{
    public static MediaDto ToDto(this MediaFile media, IFileStorage storage) => new(
        media.Id,
        storage.GetPublicUrl(media.StorageKey),
        media.Width,
        media.Height,
        media.ContentType,
        media.SizeBytes,
        media.OriginalFileName,
        media.AltText,
        media.Caption,
        media.CreatedAt,
        media.Variants
            .OrderBy(v => v.Width)
            .ToDictionary(v => v.Name, v => new MediaVariantDto(storage.GetPublicUrl(v.StorageKey), v.Width, v.Height),
                StringComparer.Ordinal));

    /// <summary>Berilgan variant URL'i; variant yo'q bo'lsa (kichik rasm yoki GIF) asl fayl URL'i.</summary>
    public static string GetUrl(this MediaFile media, IFileStorage storage, string? preferredVariant = null)
    {
        var variant = preferredVariant is null ? null : media.GetVariant(preferredVariant);
        return storage.GetPublicUrl(variant?.StorageKey ?? media.StorageKey);
    }

    /// <summary>Asl fayl va variantlarning barcha storage kalitlari (o'chirish uchun).</summary>
    public static IReadOnlyList<string> AllStorageKeys(this MediaFile media) =>
        [media.StorageKey, .. media.Variants.Select(v => v.StorageKey)];
}
