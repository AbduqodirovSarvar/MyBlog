using Microsoft.Extensions.Logging;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Application.Features.Auth.Jobs;

/// <summary>Har kuni muddati o'tgan refresh token'larni o'chiradi.</summary>
internal sealed class ExpiredRefreshTokensCleanupJob(
    IRefreshTokenService refreshTokenService,
    ILogger<ExpiredRefreshTokensCleanupJob> logger) : IRecurringJob
{
    public string Name => "auth:expired-refresh-tokens-cleanup";

    public TimeSpan Interval => TimeSpan.FromDays(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var removed = await refreshTokenService.RemoveExpiredAsync(cancellationToken);
        if (removed > 0)
            logger.LogInformation("Removed {Count} expired refresh tokens", removed);
    }
}
