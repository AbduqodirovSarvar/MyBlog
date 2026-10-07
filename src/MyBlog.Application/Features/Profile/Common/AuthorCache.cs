using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.Common;

/// <summary>Muallifning ommaviy ma'lumotlari keshi: kalitlar va tag ("author:{username}").</summary>
public static class AuthorCacheKeys
{
    public static readonly TimeSpan Expiration = TimeSpan.FromMinutes(5);

    /// <summary>Muallif bilan bog'liq barcha kesh yozuvlari shu tag bilan belgilanadi.</summary>
    public static string Tag(string username) => $"author:{Normalize(username)}";

    public static string Profile(string username) => $"author:{Normalize(username)}:profile";

    public static string Categories(string username, string culture) => $"author:{Normalize(username)}:categories:{culture}";

    public static string Tags(string username) => $"author:{Normalize(username)}:tags";

    private static string Normalize(string username) => username.Trim().ToLowerInvariant();
}

/// <summary>Joriy foydalanuvchining ommaviy keshini tozalaydi (profil/kategoriya/teg o'zgarganda).</summary>
internal interface IAuthorCacheInvalidator
{
    Task InvalidateAsync(string username, CancellationToken cancellationToken = default);

    Task InvalidateCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>User id bo'yicha (background job'lar kabi joriy user yo'q joylar uchun).</summary>
    Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

internal sealed class AuthorCacheInvalidator(
    IReadRepository<UserProfile> profiles,
    ICurrentUser currentUser,
    ICacheService cache) : IAuthorCacheInvalidator
{
    public Task InvalidateAsync(string username, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(AuthorCacheKeys.Tag(username), cancellationToken);

    public Task InvalidateCurrentUserAsync(CancellationToken cancellationToken = default) =>
        currentUser.Id is { } userId ? InvalidateUserAsync(userId, cancellationToken) : Task.CompletedTask;

    public async Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var username = await profiles.FirstOrDefaultAsync(new ProfileUsernameSpec(userId), cancellationToken);
        if (username is not null)
            await InvalidateAsync(username, cancellationToken);
    }
}
