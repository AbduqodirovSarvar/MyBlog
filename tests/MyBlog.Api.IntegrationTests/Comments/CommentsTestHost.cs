using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Api.IntegrationTests.Comments;

/// <summary>
/// Test autentifikatsiyasi: "X-Test-User" (user id) va "X-Test-Permissions" (vergul bilan) header'laridan claim'lar.
/// Auth modulidan (JWT) mustaqil — default sxema PostConfigure orqali almashtiriladi.
/// </summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string PermissionsHeader = "X-Test-Permissions";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", userId.ToString()) };
        var permissions = Request.Headers[PermissionsHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Alohida baza, test autentifikatsiyasi va seed qilingan foydalanuvchilar/post bilan API host.</summary>
internal sealed class CommentsTestHost : IAsyncDisposable
{
    public static readonly string[] UserPermissions = [Permissions.Comments.Write, Permissions.Reactions.Write];

    private readonly string _connectionString;

    private CommentsTestHost(string connectionString, WebApplicationFactory<Program> factory)
    {
        _connectionString = connectionString;
        Factory = factory;
    }

    public WebApplicationFactory<Program> Factory { get; }

    public Guid PostId { get; private set; }
    public Guid OwnerId { get; } = Guid.CreateVersion7();
    public Guid AliceId { get; } = Guid.CreateVersion7();
    public Guid BobId { get; } = Guid.CreateVersion7();
    public List<Guid> ExtraUserIds { get; } = [];

    /// <param name="publicRead">DataIsolation:PublicReadOfPublishedContent (false — yopiq tizim).</param>
    public static async Task<CommentsTestHost> StartAsync(PostgresFixture postgres, int extraUsers = 0, bool publicRead = true)
    {
        var connectionString = postgres.ConnectionStringFor($"comments_{Guid.NewGuid():N}");

        // Migratsiyalar bo'lmasa sxemani EnsureCreated bilan yaratamiz (bo'lsa host startup'da Migrate qiladi).
        await using (var schema = new AppDbContext(AppDbContextFactory.CreateDesignTimeOptions(connectionString),
                         DisabledDataIsolationContext.Instance))
        {
            if (!schema.Database.GetMigrations().Any())
                await schema.Database.EnsureCreatedAsync();
        }

        var factory = new ApiFactory(connectionString).WithWebHostBuilder(builder => builder
            .UseSetting("DataIsolation:PublicReadOfPublishedContent", publicRead ? "true" : "false")
            .ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
        }));

        var host = new CommentsTestHost(connectionString, factory);
        await host.SeedAsync(extraUsers);
        return host;
    }

    private async Task SeedAsync(int extraUsers)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AddUserWithProfile(OwnerId, "owner");
        db.AddUserWithProfile(AliceId, "alice");
        db.AddUserWithProfile(BobId, "bob");
        for (var i = 0; i < extraUsers; i++)
        {
            var id = Guid.CreateVersion7();
            ExtraUserIds.Add(id);
            db.AddUserWithProfile(id, $"user{i}");
        }

        var post = Post.Create(OwnerId, "Integration post", "integration-post", PostContent.Empty(), 1).Value;
        post.Publish(DateTimeOffset.UtcNow);
        db.Add(post);
        PostId = post.Id;

        await db.SaveChangesAsync();
    }

    public HttpClient Client(Guid? userId, params string[] permissions)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, id.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader,
                string.Join(',', permissions.Length == 0 ? UserPermissions : permissions));
        }

        return client;
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<Post> LoadPostAsync() =>
        QueryAsync(db => db.Set<Post>().IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == PostId));

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();

        await using var db = new AppDbContext(AppDbContextFactory.CreateDesignTimeOptions(_connectionString),
            DisabledDataIsolationContext.Instance);
        await db.Database.EnsureDeletedAsync();
    }
}
