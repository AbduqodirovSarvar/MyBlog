using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Media;

namespace MyBlog.Application.Features.Media;

/// <summary>Joriy foydalanuvchining media fayllari (ownership filtri), eng yangisidan.</summary>
internal sealed class MyMediaPageSpec : Specification<MediaFile>
{
    public MyMediaPageSpec(string? type, int page, int pageSize, bool forCount = false)
    {
        if (DomainTypeFilter(type) is { } filter)
        {
            if (filter.EndsWith('/'))
                Where(m => m.ContentType.StartsWith(filter));
            else
                Where(m => m.ContentType == filter);
        }

        if (forCount)
            return;

        OrderByDesc(m => m.CreatedAt);
        OrderByDesc(m => m.Id);
        Paginate(page, pageSize);
        ReadOnly();
    }

    /// <summary>"image" → "image/" prefiksi, "image/gif" → aniq tur.</summary>
    private static string? DomainTypeFilter(string? type)
    {
        var value = type?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
            return null;
        return value.Contains('/') ? value : value + "/";
    }
}

/// <summary>
/// Berilgan egaga tegishli media'lar (id'lar bo'yicha). Ownership filtri o'rniga aniq OwnerId sharti:
/// kontent pipeline'i va boshqa modullar ishlatadi.
/// </summary>
public sealed class MediaByIdsForOwnerSpec : Specification<MediaFile>
{
    public MediaByIdsForOwnerSpec(Guid ownerId, IReadOnlyCollection<Guid> ids)
    {
        Where(m => m.OwnerId == ownerId && ids.Contains(m.Id));
        IgnoreOwnership();
        ReadOnly();
    }
}

/// <summary>Id'lar bo'yicha media (egasidan qat'i nazar) — public sahifalarda URL hosil qilish uchun.</summary>
public sealed class MediaByIdsSpec : Specification<MediaFile>
{
    public MediaByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Where(m => ids.Contains(m.Id));
        IgnoreOwnership();
        ReadOnly();
    }
}
