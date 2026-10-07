using System.Globalization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Infrastructure.Caching;

namespace MyBlog.Infrastructure.Tests.Cache;

public sealed class HybridCacheServiceTests
{
    [Fact]
    public async Task Factory_runs_with_the_callers_ui_culture()
    {
        await using var provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var service = new HybridCacheService(provider.GetRequiredService<HybridCache>());

        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
        try
        {
            var cultureInsideFactory = await service.GetOrCreateAsync(
                $"culture-test:{Guid.NewGuid()}",
                _ => Task.FromResult(CultureInfo.CurrentUICulture.Name),
                cancellationToken: TestContext.Current.CancellationToken);

            cultureInsideFactory.ShouldBe("ru");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
