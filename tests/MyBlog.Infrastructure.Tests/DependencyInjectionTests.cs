using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyBlog.Application;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Identity;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Interceptors;
using MyBlog.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests;

// Konvensiya testi uchun: nomi "Repository" bilan tugaydi va Application interfeysini implementatsiya qiladi.
internal sealed class SampleRepository : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default) => operation(cancellationToken);
}

internal sealed class SampleService : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default) => operation(cancellationToken);
}

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Conventional_repositories_are_registered_for_application_interfaces()
    {
        var services = new ServiceCollection();

        services.AddConventionalRepositories(typeof(DependencyInjectionTests).Assembly);

        var registration = services.ShouldHaveSingleItem();
        registration.ServiceType.ShouldBe(typeof(IUnitOfWork));
        registration.ImplementationType.ShouldBe(typeof(SampleRepository));
        registration.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Generic_ef_repositories_are_not_picked_up_by_convention()
    {
        var services = new ServiceCollection();

        services.AddConventionalRepositories(typeof(AppDbContext).Assembly);

        services.ShouldNotContain(d => d.ImplementationType == typeof(EfRepository<>) || d.ImplementationType == typeof(EfReadRepository<>));
    }

    [Fact]
    public async Task AddInfrastructure_wires_services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=di_only",
                ["Smtp:Host"] = "localhost",
                ["Smtp:FromAddress"] = "no-reply@myblog.local"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddApplication();
        services.AddInfrastructure(configuration, environment);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IUnitOfWork>().ShouldBeOfType<AppDbContext>();
        sp.GetRequiredService<IReadRepository<RefreshToken>>().ShouldBeOfType<EfReadRepository<RefreshToken>>();
        sp.GetRequiredService<ILocalizer>().DefaultCulture.ShouldBe("uz");
        sp.GetRequiredService<ISlugGenerator>().ShouldNotBeNull();
        sp.GetRequiredService<IHtmlSanitizer>().ShouldNotBeNull();
        sp.GetRequiredService<IFileStorage>().ShouldNotBeNull();
        sp.GetRequiredService<ICacheService>().ShouldNotBeNull();
        sp.GetRequiredService<IEmailQueue>().ShouldNotBeNull();
        sp.GetRequiredService<IEmailTemplateRenderer>().ShouldNotBeNull();
        sp.GetRequiredService<IDataIsolationContext>().IsEnabled.ShouldBeTrue();

        sp.GetServices<ISaveChangesInterceptor>().Select(i => i.GetType()).ShouldBe(
        [
            typeof(OwnershipInterceptor), typeof(SoftDeleteInterceptor), typeof(AuditingInterceptor), typeof(DomainEventsInterceptor)
        ]);
    }

    [Fact]
    public async Task Cache_service_returns_cached_values()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddSingleton<ICacheService, Caching.HybridCacheService>();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        var ct = TestContext.Current.CancellationToken;
        var calls = 0;

        Task<int> Factory(CancellationToken _) => Task.FromResult(++calls);

        (await cache.GetOrCreateAsync("k", Factory, TimeSpan.FromMinutes(1), ["t"], ct)).ShouldBe(1);
        (await cache.GetOrCreateAsync("k", Factory, TimeSpan.FromMinutes(1), ["t"], ct)).ShouldBe(1);

        await cache.RemoveByTagAsync("t", ct);
        (await cache.GetOrCreateAsync("k", Factory, TimeSpan.FromMinutes(1), ["t"], ct)).ShouldBe(2);

        await cache.RemoveAsync("k", ct);
        (await cache.GetOrCreateAsync("k", Factory, cancellationToken: ct)).ShouldBe(3);
    }
}
