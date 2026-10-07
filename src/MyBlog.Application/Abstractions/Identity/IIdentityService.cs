using MyBlog.Application.Common.Models;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Abstractions.Identity;

/// <summary>
/// Identity (foydalanuvchi, parol, rol) bilan ishlash. Infrastructure'da UserManager/RoleManager orqali
/// implementatsiya qilinadi. Email/parol tokenlari Base64Url ko'rinishida qaytadi va shu ko'rinishda qabul qilinadi.
/// </summary>
public interface IIdentityService
{
    /// <summary>Yangi foydalanuvchi ("User" roli bilan). Xatolar: EmailTaken, UsernameTaken, parol siyosati.</summary>
    Task<Result<Guid>> CreateUserAsync(string email, string userName, string password, CancellationToken cancellationToken = default);

    Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>"@" bo'lsa email, aks holda username bo'yicha qidiradi.</summary>
    Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default);

    Task<bool> IsEmailTakenAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> IsUserNameTakenAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Login uchun: foydalanuvchini ("@" bo'lsa email, aks holda username) topib parolni tekshiradi. Foydalanuvchi
    /// topilmasa ham parol xeshi tekshiriladi (javob vaqti mavjudlikni oshkor qilmasin). Bloklangan/tasdiqlanmagan
    /// holat faqat parol to'g'ri bo'lganda qaytadi; lockout paytida parol hisoblagichi o'zgarmaydi.
    /// </summary>
    Task<PasswordCheckResult> CheckCredentialsAsync(string emailOrUserName, string password,
        CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Parolni tiklaydi; security stamp yangilanadi va lockout bekor qilinadi.</summary>
    Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Bloklash/blokdan chiqarish; security stamp yangilanadi (mavjud access token'lar bekor bo'ladi).</summary>
    Task<Result> SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default);

    /// <summary>Security stamp'ni yangilaydi: foydalanuvchining barcha mavjud access token'lari bekor bo'ladi.</summary>
    Task<Result> InvalidateSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Rol qo'shish/olib tashlash security stamp'ni yangilaydi (token'dagi rollar eskiradi).</summary>
    Task<Result> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);
    Task<Result> RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);
    Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default);

    Task<UserSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedList<UserSummary>> ListUsersAsync(UserListFilter filter, CancellationToken cancellationToken = default);
}

public interface ITokenService
{
    /// <summary>JWT access token: sub, email, unique_name, jti, sv (sessiya versiyasi), role(lar), permission(lar).</summary>
    AccessToken CreateAccessToken(AuthUser user);
}

/// <summary>
/// Refresh token'lar: bazada faqat SHA-256 hash saqlanadi. Har bir ishlatishda rotatsiya qilinadi;
/// allaqachon almashtirilgan token qayta ishlatilsa — foydalanuvchining barcha tokenlari bekor qilinadi.
/// </summary>
public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Xatolar: AuthErrors.InvalidRefreshToken (topilmadi, muddati o'tgan, bekor qilingan yoki qayta ishlatilgan).</summary>
    Task<Result<RefreshTokenRotation>> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <returns>Token topilib bekor qilingan bo'lsa true.</returns>
    Task<bool> RevokeAsync(string refreshToken, string reason, CancellationToken cancellationToken = default);

    Task<int> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Muddati o'tgan tokenlarni o'chiradi.</summary>
    Task<int> RemoveExpiredAsync(CancellationToken cancellationToken = default);
}
