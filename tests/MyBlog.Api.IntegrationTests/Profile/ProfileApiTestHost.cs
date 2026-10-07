using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Api.IntegrationTests.Profile;

/// <summary>
/// Header orqali autentifikatsiya (JWT'siz): "X-Test-User" — user id, "X-Test-Permissions" — vergul bilan ruxsatlar.
/// Header bo'lmasa so'rov anonim.
/// </summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string PermissionsHeader = "X-Test-Permissions";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        List<Claim> claims = [new("sub", userId.ToString()), new(ClaimTypes.NameIdentifier, userId.ToString())];
        var permissions = Request.Headers[PermissionsHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Alohida baza, sxema va seed qilingan profillar bilan test host.</summary>
internal sealed class ProfileApiTestHost : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly WebApplicationFactory<Program> _factory;

    private ProfileApiTestHost(string connectionString, WebApplicationFactory<Program> factory)
    {
        _connectionString = connectionString;
        _factory = factory;
    }

    public static async Task<ProfileApiTestHost> CreateAsync(PostgresFixture postgres, params (Guid Id, string Username)[] users)
    {
        var connectionString = postgres.ConnectionStringFor($"profile_{Guid.NewGuid():N}");

        // Sxema host ishga tushishidan oldin (seeder'lar bazaga murojaat qilishi mumkin)
        await using (var db = CreateContext(connectionString))
        {
            if (db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync();
            else
                await db.Database.EnsureCreatedAsync();

            foreach (var (id, username) in users)
                db.AddUserWithProfile(id, username);
            await db.SaveChangesAsync();
        }

        var factory = new ApiFactory(connectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    o.DefaultForbidScheme = TestAuthHandler.SchemeName;
                });
            }));

        return new ProfileApiTestHost(connectionString, factory);
    }

    public HttpClient Anonymous() => _factory.CreateClient();

    /// <summary>Profile/Categories/Tags/Media ruxsatlari bilan foydalanuvchi.</summary>
    public HttpClient As(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',',
            Permissions.Profile.Manage, Permissions.Categories.Manage, Permissions.Tags.Manage, Permissions.Media.Manage));
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var db = CreateContext(_connectionString);
        await db.Database.EnsureDeletedAsync();
    }

    private static AppDbContext CreateContext(string connectionString) =>
        new(AppDbContextFactory.CreateDesignTimeOptions(connectionString), DisabledDataIsolationContext.Instance);
}
