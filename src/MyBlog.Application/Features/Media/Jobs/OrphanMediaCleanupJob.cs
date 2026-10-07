using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Application.Features.Media.Manage;

namespace MyBlog.Application.Features.Media.Jobs;

/// <summary>
/// Har soatda: <see cref="MediaOptions.OrphanRetentionHours"/> dan eski va hech qayerda ishlatilmayotgan media'larni
/// (yozuv + fayllar) o'chiradi. Foydalanuvchisiz ishlaydi, shuning uchun repository ownership filtrini chetlab o'tadi.
/// </summary>
internal sealed class OrphanMediaCleanupJob(
    IMediaRepository mediaRepository,
    IFileStorage storage,
    IOptions<MediaOptions> options,
    TimeProvider timeProvider,
    ILogger<OrphanMediaCleanupJob> logger) : IRecurringJob
{
    public string Name => "media-orphan-cleanup";

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow().AddHours(-settings.OrphanRetentionHours);

        var candidates = await mediaRepository.ListUnreferencedAsync(cutoff, settings.OrphanCleanupBatchSize, cancellationToken);
        var deleted = 0;

        foreach (var media in candidates)
        {
            // Tanlash va o'chirish orasida post'ga qo'shilgan bo'lishi mumkin — o'chirish sharti qayta tekshiradi.
            if (!await mediaRepository.DeleteIfUnreferencedAsync(media.Id, cancellationToken))
                continue;

            await MediaFileCleanup.DeleteFilesAsync(storage, media, logger, cancellationToken);
            deleted++;
        }

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} orphan media files", deleted);
    }
}
