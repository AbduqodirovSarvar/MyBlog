using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Media.Upload;

/// <param name="Content">Fayl oqimi (handler uni o'qiydi, yopmaydi).</param>
/// <param name="Length">Mijoz e'lon qilgan hajm (bayt).</param>
public sealed record UploadMediaCommand(
    Stream Content,
    string FileName,
    string ContentType,
    long Length,
    string? AltText = null,
    string? Caption = null) : ICommand<MediaDto>;

internal sealed class UploadMediaCommandValidator : AbstractValidator<UploadMediaCommand>
{
    public UploadMediaCommandValidator()
    {
        RuleFor(x => x.Length).GreaterThan(0).WithErrorCode(MediaErrors.FileRequired.Code).WithMessage(MediaErrors.FileRequired.Description);
        RuleFor(x => x.FileName).NotEmpty().WithErrorCode(MediaErrors.FileNameRequired.Code).WithMessage(MediaErrors.FileNameRequired.Description);
    }
}

internal sealed class UploadMediaCommandHandler(
    IRepository<MediaFile> repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IFileSignatureValidator signatureValidator,
    IOptions<MediaOptions> options,
    TimeProvider timeProvider,
    ILogger<UploadMediaCommandHandler> logger)
    : ICommandHandler<UploadMediaCommand, MediaDto>
{
    public async Task<Result<MediaDto>> Handle(UploadMediaCommand request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var ownerId = currentUser.RequiredId;

        if (request.Length > settings.MaxUploadBytes)
            return MediaErrors.FileTooLarge.WithArgs(settings.MaxUploadBytes);

        var declaredType = signatureValidator.NormalizeContentType(request.ContentType ?? string.Empty);
        if (!settings.EffectiveContentTypes.Contains(declaredType, StringComparer.OrdinalIgnoreCase))
            return MediaErrors.UnsupportedFileType;

        // Hajm cheklangan, shuning uchun xotiraga o'qish xavfsiz; e'lon qilingan Length'ga ishonmaymiz.
        var read = await ReadLimitedAsync(request.Content, settings.MaxUploadBytes, cancellationToken);
        if (read is null)
            return MediaErrors.FileTooLarge.WithArgs(settings.MaxUploadBytes);
        if (read.Length == 0)
            return MediaErrors.FileRequired;

        var header = read.AsSpan(0, Math.Min(read.Length, signatureValidator.HeaderLength));
        if (!signatureValidator.Matches(header, declaredType))
            return MediaErrors.FileContentMismatch;

        var variants = settings.Variants
            .Where(v => v.Value > 0)
            .Select(v => new ImageVariantSpec(v.Key.ToLowerInvariant(), v.Value))
            .ToList();

        var processed = imageProcessor.Process(read,
            new ImageProcessingRequest(declaredType, variants, settings.Quality, settings.OriginalQuality, settings.MaxPixels));
        if (processed.IsFailure)
            return processed.Error;

        var image = processed.Value;
        var now = timeProvider.GetUtcNow();
        var baseName = $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}";
        var originalKey = $"{baseName}.{image.Original.Extension}";

        var mediaVariants = new List<MediaVariant>(image.Variants.Count);
        foreach (var variant in image.Variants)
        {
            var created = MediaVariant.Create(variant.Name, $"{baseName}_{variant.Name}.{variant.Image.Extension}",
                variant.Image.Width, variant.Image.Height, variant.Image.Content.LongLength, variant.Image.ContentType);
            if (created.IsFailure)
                return created.Error;
            mediaVariants.Add(created.Value);
        }

        var mediaResult = MediaFile.Create(ownerId, request.FileName, image.Original.ContentType,
            image.Original.Content.LongLength, image.Original.Width, image.Original.Height, originalKey, mediaVariants);
        if (mediaResult.IsFailure)
            return mediaResult.Error;

        var media = mediaResult.Value;
        var metadata = media.UpdateMetadata(request.AltText, request.Caption);
        if (metadata.IsFailure)
            return metadata.Error;

        var savedKeys = new List<string>(1 + mediaVariants.Count);
        try
        {
            savedKeys.Add(await SaveAsync(originalKey, image.Original, cancellationToken));
            foreach (var variant in image.Variants)
            {
                var key = media.GetVariant(variant.Name)!.StorageKey;
                savedKeys.Add(await SaveAsync(key, variant.Image, cancellationToken));
            }

            repository.Add(media);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Baza yozuvi bo'lmasa fayllar yetim qolmasin.
            await DeleteFilesQuietlyAsync(savedKeys);
            throw;
        }

        return media.ToDto(storage);
    }

    private async Task<string> SaveAsync(string key, EncodedImage image, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(image.Content, writable: false);
        return await storage.SaveAsync(stream, key, image.ContentType, cancellationToken);
    }

    private async Task DeleteFilesQuietlyAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete media file {StorageKey} after a failed upload", key);
            }
        }
    }

    /// <summary>Limitdan oshsa null.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream source, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
