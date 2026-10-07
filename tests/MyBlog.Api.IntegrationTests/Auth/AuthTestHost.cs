using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Api.IntegrationTests.Auth;

/// <summary>Email'larni yubormasdan xotirada ushlab qoladi.</summary>
public sealed partial class CapturingEmailQueue : IEmailQueue
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => [.. _messages];

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    /// <summary>Oluvchiga yuborilgan, havolasi berilgan yo'lni o'z ichiga olgan oxirgi xatdagi havolaning query parametrlari.</summary>
    public Dictionary<string, string> LinkQuery(string to, string pathFragment)
    {
        var link = Messages
            .Where(m => m.To == to)
            .SelectMany(m => HrefRegex().Matches(m.HtmlBody).Select(x => WebUtility.HtmlDecode(x.Groups[1].Value)))
            .Last(href => href.Contains(pathFragment, StringComparison.Ordinal));

        return QueryHelpers.ParseQuery(new Uri(link).Query).ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    [GeneratedRegex("href=\"([^\"]+)\"")]
    private static partial Regex HrefRegex();
}

/// <summary>Har bir test uchun alohida baza va email'larni ushlaydigan host.</summary>
internal sealed class AuthTestHost : IAsyncDisposable
{
    public const string Password = "Secret123";

    private readonly ApiFactory _apiFactory;
    private readonly WebApplicationFactory<Program> _factory;

    private AuthTestHost(string connectionString, Action<IServiceCollection>? configureServices)
    {
        _apiFactory = new ApiFactory(connectionString);
        _factory = _apiFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailQueue>();
            services.AddSingleton(Emails);
            services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<CapturingEmailQueue>());
            configureServices?.Invoke(services);
        }));
        Client = _factory.CreateClient();
    }

    public IServiceProvider Services => _factory.Services;

    public CapturingEmailQueue Emails { get; } = new();

    public HttpClient Client { get; }

    public static async Task<AuthTestHost> CreateAsync(PostgresFixture postgres,
        Action<IServiceCollection>? configureServices = null)
    {
        var connectionString = postgres.ConnectionStringFor($"auth_{Guid.NewGuid():N}");

        // Migratsiyalar hali yo'q bo'lsa sxema EnsureCreated bilan yaratiladi (bor bo'lsa — startup'da qo'llanadi).
        await using (var db = new AppDbContext(AppDbContextFactory.CreateDesignTimeOptions(connectionString),
                         DisabledDataIsolationContext.Instance))
        {
            if (!db.Database.GetMigrations().Any())
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        return new AuthTestHost(connectionString, configureServices);
    }

    public Task<HttpResponseMessage> PostAsync(string url, object body, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<HttpResponseMessage> GetAsync(string url, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Ro'yxatdan o'tkazadi va email'dagi havola orqali tasdiqlaydi.</summary>
    public async Task<Guid> RegisterConfirmedAsync(string userName, string? email = null)
    {
        email ??= $"{userName}@example.com";
        var register = await PostAsync("/api/auth/register", new
        {
            email,
            userName,
            password = Password,
            confirmPassword = Password,
            culture = "en"
        });
        register.StatusCode.ShouldBe(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());

        var query = Emails.LinkQuery(email, "/en/auth/confirm-email");
        var confirm = await PostAsync("/api/auth/confirm-email", new { userId = query["userId"], token = query["token"] });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirm.Content.ReadAsStringAsync());

        return Guid.Parse(query["userId"]);
    }

    public async Task<AuthResponse> LoginAsync(string emailOrUserName, string password = Password)
    {
        var response = await PostAsync("/api/auth/login", new { emailOrUserName, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(TestContext.Current.CancellationToken))!;
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _apiFactory.DisposeAsync();
    }
}
