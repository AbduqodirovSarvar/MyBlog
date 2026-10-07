using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Features.Auth;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Identity;

/// <summary>
/// Refresh token'lar. Rotatsiya: eski token atomar (ExecuteUpdate + RevokedAt IS NULL sharti) bekor qilinadi va
/// yangisiga bog'lanadi. Almashtirilgan token qayta kelsa — foydalanuvchining barcha faol tokenlari bekor qilinadi.
/// </summary>
internal sealed class RefreshTokenService(
    AppDbContext dbContext,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    internal const string RotatedReason = "Rotated";
    internal const string ReuseDetectedReason = "Reuse detected";

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (entity, issued) = CreateToken(userId);
        dbContext.RefreshTokens.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return issued;
    }

    public async Task<Result<RefreshTokenRotation>> RotateAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return AuthErrors.InvalidRefreshToken;

        var hash = RefreshTokenCrypto.Hash(refreshToken.Trim());
        var stored = await dbContext.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var now = timeProvider.GetUtcNow();
        switch (RefreshTokenPolicy.Evaluate(stored, now))
        {
            case RefreshTokenState.Active:
                break;

            case RefreshTokenState.Reused:
                logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", stored!.UserId);
                await RevokeAllForUserAsync(stored.UserId, ReuseDetectedReason, cancellationToken);
                return AuthErrors.InvalidRefreshToken;

            default:
                return AuthErrors.InvalidRefreshToken;
        }

        return await dbContext.ExecuteInTransactionAsync<Result<RefreshTokenRotation>>(async ct =>
        {
            var (entity, issued) = CreateToken(stored!.UserId);

            // Parallel so'rovlardan faqat bittasi muvaffaqiyatli bo'ladi.
            var affected = await dbContext.RefreshTokens
                .Where(t => t.Id == stored.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.ReplacedByTokenHash, entity.TokenHash)
                    .SetProperty(t => t.RevokedReason, RotatedReason), ct);

            if (affected == 0)
                return AuthErrors.InvalidRefreshToken;

            dbContext.RefreshTokens.Add(entity);
            await dbContext.SaveChangesAsync(ct);
            return new RefreshTokenRotation(stored.UserId, issued);
        }, cancellationToken);
    }

    public async Task<bool> RevokeAsync(string refreshToken, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        var hash = RefreshTokenCrypto.Hash(refreshToken.Trim());
        var now = timeProvider.GetUtcNow();
        var revokedReason = Truncate(reason);

        var affected = await dbContext.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, revokedReason), cancellationToken);

        return affected > 0;
    }

    public Task<int> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var revokedReason = Truncate(reason);
        return dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, revokedReason), cancellationToken);
    }

    public Task<int> RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return dbContext.RefreshTokens.Where(t => t.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }

    private (RefreshToken Entity, IssuedRefreshToken Issued) CreateToken(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(options.Value.RefreshTokenDays);
        var token = RefreshTokenCrypto.Generate();

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = RefreshTokenCrypto.Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
            CreatedByIp = Truncate(httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), 64)
        };

        return (entity, new IssuedRefreshToken(token, expiresAt));
    }

    private static string? Truncate(string? value, int maxLength = 256) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
