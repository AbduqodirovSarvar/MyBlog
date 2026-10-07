using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Infrastructure.Content.Processing;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Posts moduli: repository'lar, servislar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class PostsModule
{
    public static IServiceCollection AddPostsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<PostsOptions>(PostsOptions.SectionName);
        services.AddValidatedOptions<ContentPipelineOptions>(ContentPipelineOptions.SectionName);

        // Kontent pipeline (Strategy): format → IContentProcessor. Yangi muharrir formati — yangi strategiya qo'shish kifoya.
        services.AddScoped<HtmlContentPipeline>();
        services.AddScoped<IContentProcessor, HtmlContentProcessor>();
        services.AddScoped<IContentProcessor, HtmlWithRawDocumentContentProcessor>();
        services.AddScoped<IContentProcessorFactory, ContentProcessorFactory>();

        return services;
    }
}
