using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MyBlog.Api.Common;

namespace MyBlog.Api.RateLimiting;

/// <summary>
/// Rate limit policy nomlari. Controller/endpoint'da: <c>[EnableRateLimiting(RateLimitPolicies.Auth)]</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Login/register/refresh: IP bo'yicha (default 10/daqiqa).</summary>
    public const string Auth = "auth";

    /// <summary>Parolni tiklash / tasdiqlash xatini qayta yuborish: IP bo'yicha (default 3/soat).</summary>
    public const string PasswordReset = "password-reset";

    /// <summary>Izoh yozish: foydalanuvchi (yoki IP) bo'yicha (default 5/daqiqa).</summary>
    public const string Comments = "comments";

    /// <summary>Fayl yuklash: foydalanuvchi bo'yicha (default 30/daqiqa).</summary>
    public const string Upload = "upload";
}

internal sealed class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; } = 60;

    public FixedWindowRateLimiterOptions ToOptions() => new()
    {
        PermitLimit = Math.Max(PermitLimit, 1),
        Window = TimeSpan.FromSeconds(Math.Max(WindowSeconds, 1)),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}

internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>false bo'lsa hech qanday cheklov qo'llanmaydi (integration testlar uchun).</summary>
    public bool Enabled { get; set; } = true;

    public RateLimitRule Global { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };
    public RateLimitRule Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RateLimitRule PasswordReset { get; set; } = new() { PermitLimit = 3, WindowSeconds = 3600 };
    public RateLimitRule Comments { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public RateLimitRule Upload { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await ErrorProblemDetails.WriteAsync(context.HttpContext, GeneralErrors.TooManyRequests,
                    StatusCodes.Status429TooManyRequests, cancellationToken: cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                Partition(context, "global", o => o.Global, ClientIp(context)));

            options.AddPolicy(RateLimitPolicies.Auth, context =>
                Partition(context, RateLimitPolicies.Auth, o => o.Auth, ClientIp(context)));

            options.AddPolicy(RateLimitPolicies.PasswordReset, context =>
                Partition(context, RateLimitPolicies.PasswordReset, o => o.PasswordReset, ClientIp(context)));

            options.AddPolicy(RateLimitPolicies.Comments, context =>
                Partition(context, RateLimitPolicies.Comments, o => o.Comments, UserOrIp(context)));

            options.AddPolicy(RateLimitPolicies.Upload, context =>
                Partition(context, RateLimitPolicies.Upload, o => o.Upload, UserOrIp(context)));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, string policy,
        Func<RateLimitingOptions, RateLimitRule> rule, string clientKey)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        if (!options.Enabled)
            return RateLimitPartition.GetNoLimiter($"{policy}:disabled");

        var limiterOptions = rule(options).ToOptions();
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{clientKey}", _ => limiterOptions);
    }

    // Proksi ortida RemoteIpAddress'ni ForwardedHeaders middleware ("ReverseProxy" bo'limi) to'g'rilaydi.
    // Sarlavhalar to'g'ridan-to'g'ri o'qilmaydi — ishonchsiz manbadan kelgan X-Forwarded-For limitni chetlab o'tolmaydi.
    // "::ffff:1.2.3.4" va "1.2.3.4" bitta mijoz hisoblanadi.
    internal static string ClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true })
            address = address.MapToIPv4();

        return "ip:" + (address?.ToString() ?? "unknown");
    }

    private static string UserOrIp(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return userId is { Length: > 0 } ? "user:" + userId : ClientIp(context);
    }
}
