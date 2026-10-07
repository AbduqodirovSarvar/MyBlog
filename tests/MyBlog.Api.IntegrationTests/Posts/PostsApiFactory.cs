using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Features.Tags.Abstractions;

namespace MyBlog.Api.IntegrationTests.Posts;

/// <summary>
/// Posts/Media oqimi uchun host: "X-Test-User" sarlavhasidagi foydalanuvchi sifatida autentifikatsiya
/// (JWT Auth modulidan mustaqil). ITagResolver Tags modulida — bu yerda mavjud bo'lmasa bo'sh stub.
/// </summary>
public sealed class PostsApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "myblog-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Storage:RootPath", StorageRoot);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
            services.TryAddScoped<ITagResolver, NoTagsResolver>();
        });
    }

    public HttpClient CreateClientFor(Guid? userId)
    {
        var client = CreateClient();
        if (userId is { } id)
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, id.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(StorageRoot))
            Directory.Delete(StorageRoot, recursive: true);
    }

    private sealed class NoTagsResolver : ITagResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(Guid ownerId, IEnumerable<string> names, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}

internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || !Guid.TryParse(value, out var userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", userId.ToString()), new(ClaimTypes.Role, Roles.User) };
        claims.AddRange(Permissions.DefaultsByRole[Roles.User].Select(p => new Claim(Permissions.ClaimType, p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
