namespace MyBlog.Application.Abstractions.Identity;

/// <summary>Identity foydalanuvchisi (Identity tiplarisiz). Rollar va ruxsatlar bazadan o'qiladi.</summary>
/// <param name="SessionVersion">
/// Security stamp'dan olingan shaffof qiymat (xom stamp emas). Access token'ga yoziladi; stamp o'zgarsa
/// (bloklash, parol, rol, logout-all) eski token'lar keyingi so'rovdayoq rad etiladi.
/// </param>
public sealed record AuthUser(
    Guid Id,
    string Email,
    string UserName,
    bool EmailConfirmed,
    bool IsBlocked,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string SessionVersion);

public enum PasswordCheckStatus
{
    Success = 0,
    InvalidPassword = 1,
    LockedOut = 2,
    EmailNotConfirmed = 3,
    Blocked = 4
}

/// <param name="UserId">Faqat <see cref="PasswordCheckStatus.Success"/> bo'lganda.</param>
public sealed record PasswordCheckResult(PasswordCheckStatus Status, Guid? UserId = null)
{
    public static PasswordCheckResult Succeeded(Guid userId) => new(PasswordCheckStatus.Success, userId);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Mijozga beriladigan refresh token (ochiq ko'rinishda faqat bir marta qaytariladi).</summary>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenRotation(Guid UserId, IssuedRefreshToken Token);

/// <summary>Admin ro'yxati uchun foydalanuvchi ma'lumoti.</summary>
public sealed record UserSummary(
    Guid Id,
    string Email,
    string UserName,
    string? DisplayName,
    bool EmailConfirmed,
    bool IsBlocked,
    DateTimeOffset? BlockedAt,
    DateTimeOffset? LockoutEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles);

public sealed record UserListFilter(int Page, int PageSize, string? Search, string? Role, bool? IsBlocked);
