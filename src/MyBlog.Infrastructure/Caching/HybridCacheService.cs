using Microsoft.Extensions.Caching.Hybrid;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Caching;

/// <summary>HybridCache ustidagi yupqa adapter (stampede himoyasi va tag bo'yicha invalidatsiya bilan).</summary>
internal sealed class HybridCacheService(HybridCache cache) : ICacheService
{
    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var options = expiration is { } ttl
            ? new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl }
            : null;

        return await cache.GetOrCreateAsync(
            key,
            factory,
            static async (state, ct) => await state(ct),
            options,
            tags,
            cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken).AsTask();

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(tag, cancellationToken).AsTask();
}
