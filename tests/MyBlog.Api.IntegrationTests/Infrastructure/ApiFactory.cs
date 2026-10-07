using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MyBlog.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Test host: connection string Testcontainers bazasiga almashtiriladi, background job'lar va rate limiting o'chiriladi.
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string TestingEnvironment = "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Storage:RootPath", Path.Combine(Path.GetTempPath(), "myblog-tests", Guid.NewGuid().ToString("N")));

        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        builder.ConfigureServices(services => services.AddLogging());
    }
}
