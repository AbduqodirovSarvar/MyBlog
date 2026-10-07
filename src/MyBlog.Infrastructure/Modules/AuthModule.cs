using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Features.Auth;
using MyBlog.Infrastructure.Identity;
using MyBlog.Infrastructure.Identity.Seeding;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Modules;

/// <summary>Auth moduli: JWT, token servislari, seeder'lar va h.k. (Repository'lar konvensiya bo'yicha avtomatik ro'yxatdan o'tadi.)</summary>
internal static class AuthModule
{
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthApplication();

        services.AddValidatedOptions<AuthOptions>(AuthOptions.SectionName);
        services.AddValidatedOptions<FrontendOptions>(FrontendOptions.SectionName);
        services.AddValidatedOptions<JwtOptions>(JwtOptions.SectionName);
        services.AddOptions<SuperAdminSeedOptions>().BindConfiguration(SuperAdminSeedOptions.SectionName);

        services.AddHttpContextAccessor();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddIdentityTokenProviders();
        services.AddJwtAuthentication();

        services.AddScoped<IDataSeeder, RolesAndPermissionsSeeder>();
        services.AddScoped<IDataSeeder, SuperAdminSeeder>();

        return services;
    }

    /// <summary>Email tasdiqlash tokeni (default provider) va parol tiklash tokeni (alohida provider) muddatlari.</summary>
    private static IServiceCollection AddIdentityTokenProviders(this IServiceCollection services)
    {
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<AuthOptions>>((o, auth) => o.TokenLifespan = TimeSpan.FromHours(auth.Value.EmailTokenLifetimeHours));

        services.AddOptions<PasswordResetTokenProviderOptions>()
            .Configure<IOptions<AuthOptions>>((o, auth) =>
                o.TokenLifespan = TimeSpan.FromMinutes(auth.Value.PasswordResetTokenLifetimeMinutes));

        services.AddTransient<PasswordResetTokenProvider>();
        services.Configure<IdentityOptions>(o =>
        {
            o.Tokens.ProviderMap[PasswordResetTokenProviderOptions.ProviderName] =
                new TokenProviderDescriptor(typeof(PasswordResetTokenProvider));
            o.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName;
        });

        return services;
    }

    /// <summary>
    /// JWT Bearer — default sxema. MapInboundClaims=false: claim'lar "sub", "role", "permission" nomlari bilan qoladi
    /// (Api'dagi HttpCurrentUser shularni o'qiydi).
    /// </summary>
    private static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = JwtTokenService.CreateValidationParameters(jwt.Value);
            });

        return services;
    }
}
