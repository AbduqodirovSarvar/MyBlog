using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Tags;
using MyBlog.Application.Features.Tags.Abstractions;

namespace MyBlog.Application.Features.Profile;

/// <summary>
/// Profile/Categories/Tags modullarining handler bo'lmagan Application servislari.
/// Infrastructure'dagi ProfileModule hook'idan chaqiriladi (Application DI fayli o'zgarmasligi uchun).
/// </summary>
public static class ProfileFeatureServices
{
    public static IServiceCollection AddProfileFeatureServices(this IServiceCollection services)
    {
        services.TryAddScoped<ITagResolver, TagResolver>();
        services.TryAddScoped<IMediaUrlResolver, MediaUrlResolver>();
        services.TryAddScoped<IAuthorCacheInvalidator, AuthorCacheInvalidator>();
        services.TryAddScoped<ProfileCommandContext>();
        return services;
    }
}
