using FluentValidation;
using Microsoft.Extensions.Logging;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Media.Manage;

// ---------- Ro'yxat ----------

/// <param name="Type">"image" (prefiks) yoki aniq content type ("image/gif").</param>
public sealed record ListMyMediaQuery(int Page = 1, int PageSize = 24, string? Type = null) : IQuery<PagedList<MediaDto>>;

internal sealed class ListMyMediaQueryHandler(IReadRepository<MediaFile> repository, IFileStorage storage)
    : IQueryHandler<ListMyMediaQuery, PagedList<MediaDto>>
{
    public async Task<Result<PagedList<MediaDto>>> Handle(ListMyMediaQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);

        var total = await repository.CountAsync(new MyMediaPageSpec(request.Type, 1, 1, forCount: true), cancellationToken);
        var items = total == 0
            ? []
            : await repository.ListAsync(new MyMediaPageSpec(request.Type, paging.SafePage, paging.SafePageSize), cancellationToken);

        return new PagedList<MediaDto>(items.Select(m => m.ToDto(storage)).ToList(), paging.SafePage, paging.SafePageSize, total);
    }
}

// ---------- Bitta fayl ----------

public sealed record GetMyMediaQuery(Guid Id) : IQuery<MediaDto>;

internal sealed class GetMyMediaQueryHandler(IReadRepository<MediaFile> repository, IFileStorage storage)
    : IQueryHandler<GetMyMediaQuery, MediaDto>
{
    public async Task<Result<MediaDto>> Handle(GetMyMediaQuery request, CancellationToken cancellationToken)
    {
        var media = await repository.GetByIdAsync(request.Id, cancellationToken);
        return media is null ? MediaErrors.NotFound : media.ToDto(storage);
    }
}

// ---------- Alt/caption ----------

public sealed record UpdateMediaCommand(Guid Id, string? AltText, string? Caption) : ICommand<MediaDto>;

internal sealed class UpdateMediaCommandValidator : AbstractValidator<UpdateMediaCommand>
{
    public UpdateMediaCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithErrorCode(MediaErrors.NotFound.Code).WithMessage(MediaErrors.NotFound.Description);
    }
}

internal sealed class UpdateMediaCommandHandler(IRepository<MediaFile> repository, IUnitOfWork unitOfWork, IFileStorage storage)
    : ICommandHandler<UpdateMediaCommand, MediaDto>
{
    public async Task<Result<MediaDto>> Handle(UpdateMediaCommand request, CancellationToken cancellationToken)
    {
        var media = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (media is null)
            return MediaErrors.NotFound;

        var result = media.UpdateMetadata(request.AltText, request.Caption);
        if (result.IsFailure)
            return result.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return media.ToDto(storage);
    }
}

// ---------- O'chirish ----------

public sealed record DeleteMediaCommand(Guid Id) : ICommand;

/// <summary>Faqat hech qayerda ishlatilmayotgan faylni o'chiradi (aks holda Media.InUse). Fayllar DB'dan keyin o'chiriladi.</summary>
internal sealed class DeleteMediaCommandHandler(
    IReadRepository<MediaFile> repository,
    IMediaRepository mediaRepository,
    IFileStorage storage,
    ILogger<DeleteMediaCommandHandler> logger)
    : ICommandHandler<DeleteMediaCommand>
{
    public async Task<Result> Handle(DeleteMediaCommand request, CancellationToken cancellationToken)
    {
        // Ownership filtri: boshqa foydalanuvchining fayli "topilmadi".
        var media = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (media is null)
            return MediaErrors.NotFound;

        if (!await mediaRepository.DeleteIfUnreferencedAsync(media.Id, cancellationToken))
            return MediaErrors.InUse;

        await MediaFileCleanup.DeleteFilesAsync(storage, media, logger, cancellationToken);
        return Result.Success();
    }
}

internal static class MediaFileCleanup
{
    /// <summary>Storage'dan asl fayl va variantlarni o'chiradi; xatolar loglanadi (DB allaqachon commit qilingan).</summary>
    public static async Task DeleteFilesAsync(IFileStorage storage, MediaFile media, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var key in media.AllStorageKeys())
        {
            try
            {
                await storage.DeleteAsync(key, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to delete media file {StorageKey} of media {MediaId}", key, media.Id);
            }
        }
    }
}
