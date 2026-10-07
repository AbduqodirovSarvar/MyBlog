using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace MyBlog.Api.Infrastructure;

/// <summary>
/// "ReverseProxy" bo'limi. Enabled=false (default) — X-Forwarded-* sarlavhalari umuman o'qilmaydi va mijoz IP'si
/// TCP ulanishidan olinadi. Enabled=true bo'lsa sarlavhalar faqat ishonchli proksidan (loopback + KnownProxies +
/// KnownNetworks) kelganda qabul qilinadi; ro'yxat bo'sh bo'lsa faqat shu hostdagi (loopback) proksiga ishoniladi.
/// Docker/Kubernetes'da proksi boshqa konteynerda bo'lsa uning manzili yoki tarmog'i (masalan "172.18.0.0/16")
/// KnownProxies/KnownNetworks'ga yozilishi shart. ForwardLimit — nechta proksi zanjiriga ishonish (odatda 1).
/// </summary>
internal sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; set; }

    /// <summary>Ishonchli proksi IP manzillari ("10.0.0.5", "::1").</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Ishonchli tarmoqlar CIDR ko'rinishida ("10.0.0.0/8").</summary>
    public string[] KnownNetworks { get; set; } = [];

    [Range(1, 10)]
    public int ForwardLimit { get; set; } = 1;

    public bool HasValidAddresses() =>
        KnownProxies.All(p => IPAddress.TryParse(p, out _)) && KnownNetworks.All(n => System.Net.IPNetwork.TryParse(n, out _));
}

internal static class ReverseProxySetup
{
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services)
    {
        services.AddOptions<ReverseProxyOptions>()
            .BindConfiguration(ReverseProxyOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidAddresses(),
                "ReverseProxy: KnownProxies must contain IP addresses and KnownNetworks CIDR ranges (e.g. 10.0.0.0/8).")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((forwarded, proxy) => Apply(forwarded, proxy.Value));

        return services;
    }

    /// <summary>Pipeline'ning eng boshida chaqiriladi (rate limiter, log va HTTPS tekshiruvlari haqiqiy IP'ni ko'rsin).</summary>
    public static WebApplication UseReverseProxySupport(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled)
            app.UseForwardedHeaders();

        return app;
    }

    /// <summary>
    /// Faqat X-Forwarded-For va X-Forwarded-Proto (X-Forwarded-Host — yo'q: Host soxtalashtirilishi mumkin).
    /// Default ishonchli ro'yxat (loopback) saqlanadi va sozlamadagilar unga qo'shiladi.
    /// </summary>
    internal static void Apply(ForwardedHeadersOptions forwarded, ReverseProxyOptions proxy)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.ForwardLimit = proxy.ForwardLimit;

        foreach (var address in proxy.KnownProxies)
            forwarded.KnownProxies.Add(IPAddress.Parse(address));

        foreach (var network in proxy.KnownNetworks)
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
}
