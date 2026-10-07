namespace MyBlog.Application.Abstractions.Authorization;

/// <summary>
/// Boshqa foydalanuvchilarning nashr qilingan kontentini (postlar, muallif sahifalari, izohlar, reaksiyalar) o'qish siyosati.
/// <c>DataIsolation:PublicReadOfPublishedContent = false</c> bo'lsa tizim "yopiq": har kim faqat o'z ma'lumotini ko'radi,
/// boshqalarniki esa "mavjud emas" (404) deb qaraladi. Fon vazifalari, admin endpoint'lari va auth bu siyosatni ishlatmaydi.
/// </summary>
public interface IPublicContentPolicy
{
    /// <summary>Sozlama qiymati (DataIsolation:PublicReadOfPublishedContent).</summary>
    bool IsPublicReadEnabled { get; }

    /// <summary>
    /// Cheklov bo'lmasa null (public read yoqilgan, data isolation o'chiq yoki bypass rol). Aks holda faqat shu egasining
    /// kontenti ko'rinadi; anonim foydalanuvchi uchun <see cref="Guid.Empty"/> — ya'ni hech narsa.
    /// </summary>
    Guid? OwnerScope { get; }

    /// <summary>Joriy foydalanuvchi boshqa mualliflarning kontentini ko'ra oladimi.</summary>
    bool CanReadOthersContent => OwnerScope is null;

    /// <summary><paramref name="ownerId"/> egasining kontentini joriy foydalanuvchi ko'ra oladimi (o'ziniki — har doim).</summary>
    bool CanRead(Guid ownerId) => OwnerScope is not { } scope || (scope != Guid.Empty && scope == ownerId);
}
