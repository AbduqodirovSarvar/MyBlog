using System.Globalization;
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

        // HybridCache factory'ni so'rov ExecutionContext'isiz ishga tushiradi — culture oqib o'tmaydi va
        // lokalizatsiyalangan qiymatlar (kategoriya nomlari) server tilida yasalib qoladi. Shuning uchun
        // chaqiruvchining tilini saqlab, factory ichida tiklaymiz.
        var state = new FactoryState<T>(factory, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);

        return await cache.GetOrCreateAsync(
            key,
            state,
            static async (s, ct) =>
            {
                var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
                CultureInfo.CurrentCulture = s.Culture;
                CultureInfo.CurrentUICulture = s.UICulture;
                try
                {
                    return await s.Factory(ct);
                }
                finally
                {
                    CultureInfo.CurrentCulture = previous;
                    CultureInfo.CurrentUICulture = previousUi;
                }
            },
            options,
            tags,
            cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken).AsTask();

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(tag, cancellationToken).AsTask();

    private sealed record FactoryState<T>(Func<CancellationToken, Task<T>> Factory, CultureInfo Culture, CultureInfo UICulture);
}
