using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Auth.Common;

/// <summary>Auth javoblari uchun profil ma'lumotlari (display name, avatar, til).</summary>
internal sealed class UserProfileReader(
    IReadRepository<UserProfile> profiles,
    IReadRepository<MediaFile> media,
    IFileStorage fileStorage)
{
    public Task<ProfileSummary?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken) =>
        profiles.FirstOrDefaultAsync(new ProfileSummarySpec(userId), cancellationToken);

    public async Task<CurrentUserResponse> GetCurrentUserAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(user.Id, cancellationToken);

        string? avatarUrl = null;
        if (summary?.AvatarMediaId is { } mediaId
            && await media.FirstOrDefaultAsync(new MediaStorageKeySpec(mediaId), cancellationToken) is { Length: > 0 } key)
        {
            avatarUrl = fileStorage.GetPublicUrl(key);
        }

        return new CurrentUserResponse(
            user.Id,
            user.Email,
            user.UserName,
            summary?.DisplayName ?? user.UserName,
            avatarUrl,
            user.Roles,
            user.Permissions);
    }
}

/// <summary>Access + refresh token juftligini va foydalanuvchi ma'lumotini yig'adi.</summary>
internal sealed class AuthSessionIssuer(
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    UserProfileReader profileReader)
{
    public async Task<AuthResponse> IssueAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var refreshToken = await refreshTokenService.IssueAsync(user.Id, cancellationToken);
        return await CreateResponseAsync(user, refreshToken, cancellationToken);
    }

    public async Task<AuthResponse> CreateResponseAsync(AuthUser user, IssuedRefreshToken refreshToken, CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var currentUser = await profileReader.GetCurrentUserAsync(user, cancellationToken);

        return new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshToken.Token, refreshToken.ExpiresAt, currentUser);
    }
}

internal static class RefreshTokenRevokeReasons
{
    public const string Logout = "Logout";
    public const string LogoutAll = "Logout from all devices";
    public const string PasswordReset = "Password reset";
    public const string PasswordChanged = "Password changed";
    public const string Blocked = "User blocked";
    public const string UserUnavailable = "User blocked or deleted";
}
