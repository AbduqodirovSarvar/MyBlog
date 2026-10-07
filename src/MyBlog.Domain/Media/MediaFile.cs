using MyBlog.Domain.Common;
using static MyBlog.Domain.Media.MediaConstraints;

namespace MyBlog.Domain.Media;

/// <summary>
/// Yuklangan fayl (asosan rasm). Width/Height rasm bo'lmagan fayllar uchun null.
/// Variantlar (thumb/medium/large) fayl qayta ishlangandan keyin <see cref="SetVariants"/> orqali qo'yiladi.
/// </summary>
public sealed class MediaFile : AuditableEntity, IAggregateRoot, IOwnedEntity
{
    private readonly List<MediaVariant> _variants = [];

    private MediaFile() { }

    private MediaFile(Guid ownerId, string originalFileName, string contentType, long sizeBytes, int? width, int? height,
        string storageKey)
    {
        OwnerId = ownerId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        StorageKey = storageKey;
    }

    public Guid OwnerId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string? AltText { get; private set; }
    public string? Caption { get; private set; }

    public IReadOnlyCollection<MediaVariant> Variants => _variants.AsReadOnly();

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);

    public static Result<MediaFile> Create(Guid ownerId, string originalFileName, string contentType, long sizeBytes,
        int? width, int? height, string storageKey, IEnumerable<MediaVariant>? variants = null)
    {
        if (ownerId == Guid.Empty)
            return MediaErrors.InvalidOwner;

        var fileName = DomainRules.TrimToNull(originalFileName);
        var contentTypeValue = DomainRules.TrimToNull(contentType)?.ToLowerInvariant();
        var keyValue = DomainRules.TrimToNull(storageKey);

        if (fileName is null)
            return MediaErrors.FileNameRequired;
        if (fileName.Length > OriginalFileNameMaxLength)
            fileName = TruncateFileName(fileName);
        if (contentTypeValue is null || contentTypeValue.Length > ContentTypeMaxLength)
            return MediaErrors.ContentTypeInvalid;
        if (keyValue is null || keyValue.Length > StorageKeyMaxLength)
            return MediaErrors.StorageKeyInvalid;
        if (sizeBytes <= 0)
            return MediaErrors.SizeInvalid;
        if (width is <= 0 || height is <= 0 || width.HasValue != height.HasValue)
            return MediaErrors.DimensionsInvalid;

        var media = new MediaFile(ownerId, fileName, contentTypeValue, sizeBytes, width, height, keyValue);
        if (variants is not null)
            media.SetVariants(variants);

        return media;
    }

    public Result UpdateMetadata(string? altText, string? caption)
    {
        var altValue = DomainRules.TrimToNull(altText);
        var captionValue = DomainRules.TrimToNull(caption);

        if (altValue?.Length > AltTextMaxLength)
            return MediaErrors.AltTextTooLong;
        if (captionValue?.Length > CaptionMaxLength)
            return MediaErrors.CaptionTooLong;

        AltText = altValue;
        Caption = captionValue;
        return Result.Success();
    }

    /// <summary>Variantlarni almashtiradi (bir xil nomlilardan oxirgisi qoladi).</summary>
    public void SetVariants(IEnumerable<MediaVariant> variants)
    {
        var unique = variants
            .GroupBy(v => v.Name, StringComparer.Ordinal)
            .Select(g => g.Last().Copy())
            .ToList();

        _variants.Clear();
        _variants.AddRange(unique);
    }

    public MediaVariant? GetVariant(string name) => _variants.Find(v => v.Name == name);

    private static string TruncateFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Length >= OriginalFileNameMaxLength)
            return fileName[..OriginalFileNameMaxLength];

        var nameLength = OriginalFileNameMaxLength - extension.Length;
        return string.Concat(fileName.AsSpan(0, nameLength), extension);
    }
}

public static class MediaConstraints
{
    public const int OriginalFileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int StorageKeyMaxLength = 500;
    public const int AltTextMaxLength = 300;
    public const int CaptionMaxLength = 500;
    public const int VariantNameMaxLength = 32;
}

public static class MediaErrors
{
    public static readonly Error NotFound = Error.NotFound("Media.NotFound", "Media file was not found.");
    public static readonly Error InvalidOwner = Error.Validation("Media.InvalidOwner", "Media owner is required.");
    public static readonly Error FileNameRequired = Error.Validation("Media.FileNameRequired", "File name is required.");
    public static readonly Error ContentTypeInvalid = Error.Validation("Media.ContentTypeInvalid", "Content type is not valid.");
    public static readonly Error StorageKeyInvalid = Error.Validation("Media.StorageKeyInvalid", "Storage key is not valid.");
    public static readonly Error SizeInvalid = Error.Validation("Media.SizeInvalid", "File size must be greater than zero.");
    public static readonly Error DimensionsInvalid = Error.Validation("Media.DimensionsInvalid", "Image width and height must both be positive numbers.");
    public static readonly Error VariantInvalid = Error.Validation("Media.VariantInvalid", "Media variant is not valid.");

    public static readonly Error AltTextTooLong = Error.Validation("Media.AltTextTooLong", "Alt text must not exceed {0} characters.")
        .WithArgs(AltTextMaxLength);

    public static readonly Error CaptionTooLong = Error.Validation("Media.CaptionTooLong", "Caption must not exceed {0} characters.")
        .WithArgs(CaptionMaxLength);

    public static readonly Error UnsupportedFileType = Error.Validation("Media.UnsupportedFileType", "File type is not supported.");
    public static readonly Error FileTooLarge = Error.Validation("Media.FileTooLarge", "File size must not exceed {0} bytes.");
    public static readonly Error FileRequired = Error.Validation("Media.FileRequired", "A file is required.");
    public static readonly Error FileContentMismatch = Error.Validation("Media.FileContentMismatch", "File content does not match its declared type.");
    public static readonly Error InvalidImage = Error.Validation("Media.InvalidImage", "The file is not a valid image.");
    public static readonly Error ImageTooLarge = Error.Validation("Media.ImageTooLarge", "Image dimensions are too large.");
    public static readonly Error InUse = Error.Conflict("Media.InUse", "Media file is in use and cannot be deleted.");
}
