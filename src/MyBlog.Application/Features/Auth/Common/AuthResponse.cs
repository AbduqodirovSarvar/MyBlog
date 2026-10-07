namespace MyBlog.Application.Features.Auth.Common;

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string UserName,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserResponse User);

/// <summary>Foydalanuvchi mavjudligini oshkor qilmaydigan endpoint'lar uchun bir xil javob.</summary>
public sealed record MessageResponse(string Message);
