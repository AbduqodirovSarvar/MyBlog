using MyBlog.Domain.Common;
using static MyBlog.Domain.Media.MediaConstraints;

namespace MyBlog.Domain.Media;

/// <summary>Rasmning qayta o'lchamlangan varianti (value object, JSON ustunda saqlanadi).</summary>
public sealed class MediaVariant
{
    public const string Thumb = "thumb";
    public const string Medium = "medium";
    public const string Large = "large";

    private MediaVariant() { }

    private MediaVariant(string name, string storageKey, int width, int height, long sizeBytes, string contentType)
    {
        Name = name;
        StorageKey = storageKey;
        Width = width;
        Height = height;
        SizeBytes = sizeBytes;
        ContentType = contentType;
    }

    public string Name { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = null!;

    public static Result<MediaVariant> Create(string name, string storageKey, int width, int height, long sizeBytes,
        string contentType)
    {
        var nameValue = DomainRules.TrimToNull(name)?.ToLowerInvariant();
        var keyValue = DomainRules.TrimToNull(storageKey);
        var contentTypeValue = DomainRules.TrimToNull(contentType)?.ToLowerInvariant();

        if (nameValue is null || nameValue.Length > VariantNameMaxLength)
            return MediaErrors.VariantInvalid;
        if (keyValue is null || keyValue.Length > StorageKeyMaxLength)
            return MediaErrors.StorageKeyInvalid;
        if (contentTypeValue is null || contentTypeValue.Length > ContentTypeMaxLength)
            return MediaErrors.ContentTypeInvalid;
        if (width <= 0 || height <= 0)
            return MediaErrors.DimensionsInvalid;
        if (sizeBytes <= 0)
            return MediaErrors.SizeInvalid;

        return new MediaVariant(nameValue, keyValue, width, height, sizeBytes, contentTypeValue);
    }

    /// <summary>EF owned instance'ni bir nechta owner'ga bog'lab bo'lmaydi, shuning uchun nusxa.</summary>
    internal MediaVariant Copy() => new(Name, StorageKey, Width, Height, SizeBytes, ContentType);
}
