namespace MyBlog.Application.Features.Comments.Abstractions;

/// <summary>Bildirishnoma uchun foydalanuvchi email'i (Identity'da saqlanadi). Faqat tasdiqlangan email qaytariladi.</summary>
public interface IUserContactLookup
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken = default);
}
