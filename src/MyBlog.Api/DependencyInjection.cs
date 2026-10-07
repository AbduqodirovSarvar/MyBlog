using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Common;
using MyBlog.Api.Infrastructure;
using MyBlog.Api.OpenApi;
using MyBlog.Api.RateLimiting;
using MyBlog.Api.Services;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;

namespace MyBlog.Api;

internal static class DependencyInjection
{
    public const string CorsPolicy = "Frontend";

    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                // Model binding xatolari ham bir xil ko'rinishdagi ValidationError bo'lib qaytadi.
                options.InvalidModelStateResponseFactory = context =>
                {
                    const string code = "General.InvalidValue";
                    var localized = context.HttpContext.RequestServices.GetRequiredService<ILocalizer>().Find(code);

                    var errors = context.ModelState
                        .Where(e => e.Value is { Errors.Count: > 0 })
                        .SelectMany(e => e.Value!.Errors.Select(err => new FieldError(
                            ToCamelCase(e.Key),
                            code,
                            localized ?? (string.IsNullOrEmpty(err.ErrorMessage) ? "The value is invalid." : err.ErrorMessage))))
                        .ToList();

                    return new ErrorActionResult(new ValidationError(errors));
                };
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddRequestLocalizationFromLocalizer();

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition")));

        services.AddApiRateLimiting();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
        });

        return services;
    }

    /// <summary>
    /// Tillar ILocalizer'dan (Localization bo'limi) olinadi. Provider'lar tartibi: ?culture=..., Accept-Language.
    /// "uz-Cyrl-UZ" kabi tillar parent culture orqali "uz-Cyrl" ga tushadi.
    /// </summary>
    private static IServiceCollection AddRequestLocalizationFromLocalizer(this IServiceCollection services)
    {
        services.AddOptions<RequestLocalizationOptions>().Configure<ILocalizer>((options, localizer) =>
        {
            var cultures = localizer.SupportedCultures.Select(CultureInfo.GetCultureInfo).ToList();

            options.DefaultRequestCulture = new RequestCulture(localizer.DefaultCulture);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
            options.ApplyCurrentCultureToResponseHeaders = true;
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider { QueryStringKey = "culture", UIQueryStringKey = "culture" },
                new AcceptLanguageHeaderRequestCultureProvider()
            ];
        });

        return services;
    }

    private static string ToCamelCase(string key) =>
        string.Join('.', key.TrimStart('$', '.').Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
