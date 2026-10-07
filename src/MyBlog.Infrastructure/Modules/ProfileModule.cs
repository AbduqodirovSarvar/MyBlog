using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Features.Profile;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Profile moduli: repository'lar, servislar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class ProfileModule
{
    public static IServiceCollection AddProfileInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ITagResolver, media URL resolver, author cache invalidator va h.k. (Application'dagi internal implementatsiyalar).
        // IContentStatsRepository → ContentStatsRepository konvensiya bo'yicha ro'yxatdan o'tadi.
        services.AddProfileFeatureServices();
        return services;
    }
}
