using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Features.Comments;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Infrastructure.Comments;

namespace MyBlog.Infrastructure.Modules;

/// <summary>
/// Comments va Reactions moduli. CommentRepository/ReactionRepository konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.
/// </summary>
internal static class CommentsModule
{
    public static IServiceCollection AddCommentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<CommentsOptions>(CommentsOptions.SectionName);

        // "Frontend" bo'limidan faqat BaseUrl (havolalar uchun) — boshqa modullarning options'iga bog'lanmaymiz.
        services.AddOptions<CommentLinkOptions>().BindConfiguration(CommentLinkOptions.SectionName);

        services.AddScoped<IUserContactLookup, UserContactLookup>();

        return services;
    }
}
