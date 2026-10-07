using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Comments moduli: repository'lar, servislar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class CommentsModule
{
    public static IServiceCollection AddCommentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}