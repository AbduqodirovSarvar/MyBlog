using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Profile.Common;

/// <summary>MediaFile id'lari → public URL (avatar, cover, ikonka va h.k. uchun).</summary>
internal interface IMediaUrlResolver
{
    /// <param name="mediaIds">null va takror id'lar e'tiborga olinmaydi.</param>
    /// <param name="publicAccess">true — boshqa foydalanuvchining media'si (ownership filtrisiz).</param>
    /// <param name="preferredVariant">Mavjud bo'lsa shu variant (masalan "medium"), aks holda original fayl.</param>
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IEnumerable<Guid?> mediaIds, bool publicAccess,
        string? preferredVariant = null, CancellationToken cancellationToken = default);
}

internal sealed class MediaUrlResolver(IReadRepository<MediaFile> mediaFiles, IFileStorage fileStorage) : IMediaUrlResolver
{
    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IEnumerable<Guid?> mediaIds, bool publicAccess,
        string? preferredVariant = null, CancellationToken cancellationToken = default)
    {
        var ids = mediaIds
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var files = await mediaFiles.ListAsync(new MediaFilesByIdsSpec(ids, publicAccess), cancellationToken);

        return files.ToDictionary(
            f => f.Id,
            f =>
            {
                var key = preferredVariant is not null && f.GetVariant(preferredVariant) is { } variant
                    ? variant.StorageKey
                    : f.StorageKey;
                return fileStorage.GetPublicUrl(key);
            });
    }
}

internal sealed class MediaFilesByIdsSpec : Specification<MediaFile>
{
    public MediaFilesByIdsSpec(IReadOnlyCollection<Guid> ids, bool ignoreOwnership)
    {
        Where(m => ids.Contains(m.Id));
        if (ignoreOwnership)
            IgnoreOwnership();
        ReadOnly();
    }
}

internal static class MediaUrlExtensions
{
    public static string? UrlFor(this IReadOnlyDictionary<Guid, string> urls, Guid? mediaId) =>
        mediaId is { } id && urls.TryGetValue(id, out var url) ? url : null;
}
