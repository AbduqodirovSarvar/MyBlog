using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyBlog.Infrastructure.Persistence;

/// <summary>
/// Boshlang'ich ma'lumotlar (rollar, ruxsatlar, superadmin...). DI'ga
/// <c>services.AddScoped&lt;IDataSeeder, MySeeder&gt;()</c> orqali qo'shiladi; <see cref="Order"/> bo'yicha ishlaydi.
/// </summary>
internal interface IDataSeeder
{
    int Order { get; }
    Task SeedAsync(CancellationToken cancellationToken);
}

public static class DatabaseInitializer
{
    /// <summary>"Database:ApplyMigrationsOnStartup" true bo'lsa migratsiyalarni qo'llaydi, so'ng seeder'larni ishga tushiradi.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer).FullName!);
        var configuration = provider.GetRequiredService<IConfiguration>();
        var db = provider.GetRequiredService<AppDbContext>();

        if (configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
        {
            if (db.Database.GetMigrations().Any())
            {
                logger.LogInformation("Applying database migrations");
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                logger.LogWarning("No EF Core migrations found in the assembly; skipping migration step");
            }
        }

        foreach (var seeder in provider.GetServices<IDataSeeder>().OrderBy(s => s.Order))
        {
            logger.LogInformation("Running data seeder {Seeder}", seeder.GetType().Name);
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
