using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Infrastructure.Media;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Media moduli: rasm qayta ishlash, repository'lar, recurring job'lar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class MediaModule
{
    public static IServiceCollection AddMediaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<MediaOptions>(MediaOptions.SectionName);

        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IFileSignatureValidator, MagicBytesSignatureValidator>();

        return services;
    }
}
