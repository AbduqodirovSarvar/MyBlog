using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBlog.Api.Infrastructure;
using MyBlog.Api.RateLimiting;

namespace MyBlog.Api.IntegrationTests.Infrastructure;

/// <summary>"ReverseProxy" bo'limi → ForwardedHeadersOptions va rate limiter ko'radigan mijoz IP'si.</summary>
public sealed class ReverseProxySetupTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddReverseProxySupport();
        return services.BuildServiceProvider();
    }

    private static async Task<HttpContext> RunMiddlewareAsync(ForwardedHeadersOptions options, string remoteIp,
        string forwardedFor, string forwardedProto = "https")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            Options.Create(options));
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public void Defaults_are_disabled_with_single_hop()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        options.Enabled.ShouldBeFalse();
        options.ForwardLimit.ShouldBe(1);
        options.KnownProxies.ShouldBeEmpty();
        options.KnownNetworks.ShouldBeEmpty();
    }

    [Fact]
    public void Section_is_bound_to_forwarded_headers_options()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = "true",
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.5",
            ["ReverseProxy:KnownProxies:1"] = "fd00::5",
            ["ReverseProxy:KnownNetworks:0"] = "172.18.0.0/16",
            ["ReverseProxy:ForwardLimit"] = "2"
        });

        provider.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled.ShouldBeTrue();

        var forwarded = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        forwarded.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        forwarded.ForwardLimit.ShouldBe(2);
        forwarded.KnownProxies.ShouldContain(IPAddress.Parse("10.0.0.5"));
        forwarded.KnownProxies.ShouldContain(IPAddress.Parse("fd00::5"));
        forwarded.KnownIPNetworks.ShouldContain(System.Net.IPNetwork.Parse("172.18.0.0/16"));

        // Loopback (shu hostdagi proksi) default bo'yicha ishonchli bo'lib qoladi.
        forwarded.KnownProxies.ShouldContain(IPAddress.IPv6Loopback);
    }

    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip")]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.0/99")]
    [InlineData("ReverseProxy:ForwardLimit", "0")]
    public void Invalid_settings_fail_validation(string key, string value)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { [key] = value });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ReverseProxyOptions>>().Value);
    }

    [Fact]
    public async Task Forwarded_client_ip_is_used_only_when_request_comes_from_trusted_proxy()
    {
        var options = new ForwardedHeadersOptions();
        ReverseProxySetup.Apply(options, new ReverseProxyOptions { Enabled = true, KnownNetworks = ["10.0.0.0/8"] });

        var trusted = await RunMiddlewareAsync(options, "10.0.0.5", "203.0.113.7");
        trusted.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("203.0.113.7"));
        trusted.Request.Scheme.ShouldBe("https");
        RateLimitingSetup.ClientIp(trusted).ShouldBe("ip:203.0.113.7");

        // Ishonchsiz manba sarlavhani soxtalashtirsa ham IP o'zgarmaydi (rate limit chetlab o'tilmaydi).
        var spoofed = await RunMiddlewareAsync(options, "198.51.100.9", "203.0.113.7");
        spoofed.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("198.51.100.9"));
        spoofed.Request.Scheme.ShouldBe("http");
        RateLimitingSetup.ClientIp(spoofed).ShouldBe("ip:198.51.100.9");
    }

    [Fact]
    public async Task Only_the_last_hop_is_trusted_with_forward_limit_one()
    {
        var options = new ForwardedHeadersOptions();
        ReverseProxySetup.Apply(options, new ReverseProxyOptions { Enabled = true, KnownProxies = ["10.0.0.5"] });

        // Mijoz o'zi "1.1.1.1" ni yozib yuborgan, proksi haqiqiy IP'ni oxiriga qo'shgan.
        var context = await RunMiddlewareAsync(options, "10.0.0.5", "1.1.1.1, 203.0.113.7");

        context.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public async Task Without_configured_proxies_only_loopback_is_trusted()
    {
        var options = new ForwardedHeadersOptions();
        ReverseProxySetup.Apply(options, new ReverseProxyOptions { Enabled = true });

        (await RunMiddlewareAsync(options, "172.18.0.2", "203.0.113.7")).Connection.RemoteIpAddress
            .ShouldBe(IPAddress.Parse("172.18.0.2"));
        (await RunMiddlewareAsync(options, "127.0.0.1", "203.0.113.7")).Connection.RemoteIpAddress
            .ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public void Ipv4_mapped_addresses_share_a_rate_limit_partition()
    {
        var mapped = new DefaultHttpContext();
        mapped.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.7");

        RateLimitingSetup.ClientIp(mapped).ShouldBe("ip:203.0.113.7");
    }
}
