using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MyBlog.Application;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Email;
using MyBlog.Infrastructure.Identity;
using MyBlog.Infrastructure.Identity.Seeding;
using MyBlog.Infrastructure.Localization;
using MyBlog.Infrastructure.Persistence;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Identity;

public sealed class AuthModuleTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=di_only",
                ["Smtp:Host"] = "localhost",
                ["Smtp:FromAddress"] = "no-reply@myblog.local",
                ["Jwt:Issuer"] = "MyBlog",
                ["Jwt:Audience"] = "MyBlog.Client",
                ["Jwt:SigningKey"] = new string('k', 64),
                ["Auth:EmailTokenLifetimeHours"] = "48",
                ["Auth:PasswordResetTokenLifetimeMinutes"] = "30",
                ["Frontend:BaseUrl"] = "http://localhost:4200"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddApplication();
        services.AddInfrastructure(configuration, environment);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task Auth_services_seeders_and_options_are_wired()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IIdentityService>().ShouldBeOfType<IdentityService>();
        sp.GetRequiredService<ITokenService>().ShouldBeOfType<JwtTokenService>();
        sp.GetRequiredService<IRefreshTokenService>().ShouldBeOfType<RefreshTokenService>();
        sp.GetServices<IDataSeeder>().Select(s => s.GetType())
            .ShouldBe([typeof(RolesAndPermissionsSeeder), typeof(SuperAdminSeeder)], ignoreOrder: true);
        sp.GetServices<IRecurringJob>().ShouldContain(j => j.Name == "auth:expired-refresh-tokens-cleanup");

        sp.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan.ShouldBe(TimeSpan.FromHours(48));
        sp.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value.TokenLifespan.ShouldBe(TimeSpan.FromMinutes(30));
        sp.GetRequiredService<IOptions<IdentityOptions>>().Value.Tokens.PasswordResetTokenProvider
            .ShouldBe(PasswordResetTokenProviderOptions.ProviderName);
        sp.GetRequiredService<UserManager<Infrastructure.Identity.ApplicationUser>>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Jwt_bearer_is_default_scheme_and_keeps_jwt_claim_names()
    {
        await using var provider = BuildProvider();

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.ShouldBe(JwtBearerDefaults.AuthenticationScheme);

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        bearer.MapInboundClaims.ShouldBeFalse();
        bearer.TokenValidationParameters.ValidIssuer.ShouldBe("MyBlog");
        bearer.TokenValidationParameters.ValidAudience.ShouldBe("MyBlog.Client");
        bearer.TokenValidationParameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));

        // Imzodan tashqari sessiya (security stamp) ham tekshiriladi.
        bearer.Events.OnTokenValidated.ShouldBe(UserSessionTokenValidation.OnTokenValidatedAsync);

        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IUserSessionValidator>().ShouldBeOfType<UserSessionValidator>();
        scope.ServiceProvider.GetRequiredService<DummyPasswordVerifier>().ShouldNotBeNull();
    }

    [Fact]
    public void User_profile_has_cascading_foreign_key_to_identity_user()
    {
        using var context = new AppDbContext(
            AppDbContextFactory.CreateDesignTimeOptions("Host=localhost;Database=model_only"), DisabledDataIsolationContext.Instance);

        var foreignKey = context.Model.FindEntityType(typeof(UserProfile))!.GetForeignKeys()
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Infrastructure.Identity.ApplicationUser))
            .ShouldHaveSingleItem();
        foreignKey.Properties.Single().Name.ShouldBe(nameof(UserProfile.Id));
        foreignKey.IsUnique.ShouldBeTrue();
        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
    }
}

public sealed class AuthResourcesTests
{
    private static readonly string[] Cultures = ["uz", "uz-Cyrl", "ru", "en"];
    private static readonly string[] Templates = ["confirm-email", "reset-password", "password-changed", "welcome"];

    [Fact]
    public void Auth_localization_has_same_keys_in_every_culture_and_covers_all_errors()
    {
        var resources = JsonLocalizer.LoadEmbeddedResources(typeof(JsonLocalizer).Assembly);
        var keysByCulture = Cultures.ToDictionary(
            c => c,
            c => resources[c].Keys.Where(k => k.StartsWith("Auth.", StringComparison.Ordinal)).ToHashSet());

        var reference = keysByCulture["uz"];
        reference.ShouldNotBeEmpty();
        foreach (var culture in Cultures)
            keysByCulture[culture].ShouldBe(reference, ignoreOrder: true, customMessage: culture);

        var errorCodes = typeof(AuthErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => ((Error)f.GetValue(null)!).Code);
        foreach (var code in errorCodes)
            reference.ShouldContain(code);
    }

    [Fact]
    public async Task Every_auth_template_renders_in_every_culture()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.NormalizeCulture(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>()!);
        localizer.DefaultCulture.Returns("uz");
        var renderer = new EmbeddedEmailTemplateRenderer(localizer);
        var values = new Dictionary<string, string>
        {
            ["userName"] = "ali",
            ["link"] = "https://myblog.uz/x?token=abc",
            ["hours"] = "24",
            ["minutes"] = "60",
            ["time"] = "2026-10-07 12:00 UTC"
        };

        foreach (var culture in Cultures)
        {
            foreach (var template in Templates)
            {
                var email = await renderer.RenderAsync(template, culture, values, TestContext.Current.CancellationToken);

                email.Subject.ShouldNotBeNullOrWhiteSpace($"{culture}/{template}");
                email.HtmlBody.ShouldContain($"lang=\"{culture}\"");
                email.HtmlBody.ShouldContain("ali");
                email.HtmlBody.ShouldNotContain("{{");
                email.TextBody.ShouldContain("https://myblog.uz/x?token=abc");
            }
        }
    }
}
