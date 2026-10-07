namespace MyBlog.Application.Features.Authors.Abstractions;

/// <summary>
/// Post sonlari bo'yicha agregat so'rovlar (GROUP BY bazada bajariladi).
/// O'chirilgan (soft delete) postlar hisobga olinmaydi; ownership filtri chetlab o'tiladi —
/// shuning uchun egasi har doim aniq beriladi.
/// </summary>
public interface IContentStatsRepository
{
    /// <summary>Har bir muallifning nashr qilingan postlari soni (posti yo'qlar lug'atda bo'lmaydi).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPublishedPostsByOwnerAsync(IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken = default);

    /// <summary>Kategoriya bo'yicha postlar soni. <paramref name="publishedOnly"/> false — barcha statuslar.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPostsByCategoryAsync(Guid ownerId, bool publishedOnly,
        CancellationToken cancellationToken = default);

    /// <summary>Teg bo'yicha postlar soni. <paramref name="publishedOnly"/> false — barcha statuslar.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPostsByTagAsync(Guid ownerId, bool publishedOnly,
        CancellationToken cancellationToken = default);
}
