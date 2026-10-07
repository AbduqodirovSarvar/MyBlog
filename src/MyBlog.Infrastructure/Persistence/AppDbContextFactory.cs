using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Infrastructure.DataIsolation;

namespace MyBlog.Infrastructure.Persistence;

/// <summary>
/// `dotnet ef migrations add X --project src/MyBlog.Infrastructure --startup-project src/MyBlog.Api` uchun.
/// Connection string: Api'ning appsettings*.json, user-secrets yoki ConnectionStrings__Default env.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ApiUserSecretsId = "myblog-api-2f6c1a3e";

    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = FindApiDirectory();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{DependencyInjection.ConnectionStringName}' is not configured.");

        return new AppDbContext(CreateDesignTimeOptions(connectionString), DisabledDataIsolationContext.Instance);
    }

    /// <summary>
    /// DI'siz DbContext opsiyalari (migratsiya, model testlari). IdentityDbContext sxema versiyasini application
    /// service provider'dagi IdentityOptions'dan o'qiydi — runtime bilan bir xil model chiqishi uchun uni beramiz.
    /// </summary>
    internal static DbContextOptions<AppDbContext> CreateDesignTimeOptions(string connectionString)
    {
        var identityServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = AppDbContext.IdentitySchemaVersion)
            .BuildServiceProvider();

        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable(DependencyInjection.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .UseApplicationServiceProvider(identityServices)
            .Options;
    }

    private static string FindApiDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        string[] candidates =
        [
            current,
            Path.Combine(current, "src", "MyBlog.Api"),
            Path.Combine(current, "..", "MyBlog.Api")
        ];

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "appsettings.json")) && File.Exists(Path.Combine(dir, "MyBlog.Api.csproj")))
            ?? current;
    }
}
