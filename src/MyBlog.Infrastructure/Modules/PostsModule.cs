using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Posts moduli: repository'lar, servislar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class PostsModule
{
    public static IServiceCollection AddPostsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}