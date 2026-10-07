using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Tags.Abstractions;

/// <summary>
/// Teg nomlarini id'larga aylantiradi (Posts moduli uchun): slug bo'yicha topadi, yo'q bo'lsa yaratadi.
/// </summary>
public interface ITagResolver
{
    /// <summary>Bir post uchun maksimal teglar soni.</summary>
    const int MaxTags = PostConstraints.MaxTags;

    /// <summary>
    /// Nomlar trim qilinadi, bo'shlari tashlanadi, <see cref="Domain.Tags.TagConstraints.NameMaxLength"/>'gacha qisqartiriladi,
    /// slug bo'yicha takrorlar olib tashlanadi (birinchisi qoladi) va birinchi <see cref="MaxTags"/> tasi olinadi.
    /// Yangi teglar repository'ga qo'shiladi, lekin SAQLANMAYDI — chaqiruvchi o'z IUnitOfWork.SaveChangesAsync'ini chaqiradi
    /// (Id'lar oldindan ma'lum, Guid v7). Natija kirish tartibida.
    /// </summary>
    /// <param name="ownerId">Teglar egasi (odatda joriy foydalanuvchi).</param>
    Task<IReadOnlyList<Guid>> ResolveAsync(Guid ownerId, IEnumerable<string> names, CancellationToken cancellationToken = default);
}
