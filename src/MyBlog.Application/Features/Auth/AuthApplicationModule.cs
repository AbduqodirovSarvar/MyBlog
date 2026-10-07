using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Application.Features.Auth.Common;

namespace MyBlog.Application.Features.Auth;

/// <summary>
/// Auth handler'lari ishlatadigan ichki yordamchi servislar. Infrastructure'dagi AuthModule hook'idan chaqiriladi
/// (umumiy Application DependencyInjection.cs'ni o'zgartirmaslik uchun).
/// </summary>
public static class AuthApplicationModule
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<UserProfileReader>();
        services.TryAddScoped<AuthSessionIssuer>();
        services.TryAddScoped<AuthEmailService>();
        return services;
    }
}
