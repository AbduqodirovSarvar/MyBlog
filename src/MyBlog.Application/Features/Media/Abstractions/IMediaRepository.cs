using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Media.Abstractions;

/// <summary>
/// Media'ga oid maxsus so'rovlar. "Ishlatilmoqda" degani: o'chirilmagan post kontenti (PostMedia), post muqovasi,
/// SEO OG rasmi, profil avatari/muqovasi, kategoriya ikonkasi yoki sertifikat fayli. Ownership filtri hisobga olinmaydi.
/// </summary>
public interface IMediaRepository
{
    Task<bool> IsReferencedAsync(Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>Hech qayerda ishlatilmayotgan va <paramref name="createdBefore"/> dan oldin yuklangan fayllar (eng eskisidan).</summary>
    Task<IReadOnlyList<MediaFile>> ListUnreferencedAsync(DateTimeOffset createdBefore, int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bitta SQL'da (DELETE ... WHERE NOT EXISTS ...) faqat hali ishlatilmayotgan bo'lsa o'chiradi — poyga holatidan himoya.
    /// O'chirilgan bo'lsa true.
    /// </summary>
    Task<bool> DeleteIfUnreferencedAsync(Guid mediaId, CancellationToken cancellationToken = default);
}
