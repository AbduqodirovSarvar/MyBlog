using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Features.Profile;
using MyBlog.Infrastructure.DataIsolation;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Profile moduli: repository'lar, servislar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class ProfileModule
{
    public static IServiceCollection AddProfileInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ITagResolver, media URL resolver, author cache invalidator va h.k. (Application'dagi internal implementatsiyalar).
        // IContentStatsRepository → ContentStatsRepository konvensiya bo'yicha ro'yxatdan o'tadi.
        services.AddProfileFeatureServices();

        // DataIsolation:PublicReadOfPublishedContent — ommaviy endpoint'lar va handler'lar uchun.
        services.TryAddScoped<IPublicContentPolicy, PublicContentPolicy>();
        return services;
    }
}
