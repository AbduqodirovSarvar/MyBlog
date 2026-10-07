using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.BackgroundJobs;
using MyBlog.Infrastructure.Caching;
using MyBlog.Infrastructure.Content;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Email;
using MyBlog.Infrastructure.Identity;
using MyBlog.Infrastructure.Localization;
using MyBlog.Infrastructure.Modules;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Interceptors;
using MyBlog.Infrastructure.Persistence.Repositories;
using MyBlog.Infrastructure.Storage;
using MyBlog.Infrastructure.Text;

namespace MyBlog.Infrastructure;

public static class DependencyInjection
{
    internal const string ConnectionStringName = "Default";
    internal const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Infrastructure servislari. Modullar o'z servislarini Modules/*Module.cs'dagi hook'larda qo'shadi —
    /// bu faylni o'zgartirish shart emas.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddPersistence(environment)
            .AddIdentityServices(configuration)
            .AddCrossCuttingServices()
            .AddBackgroundProcessing();

        services
            .AddAuthInfrastructure(configuration)
            .AddMediaInfrastructure(configuration)
            .AddPostsInfrastructure(configuration)
            .AddProfileInfrastructure(configuration)
            .AddCommentsInfrastructure(configuration);

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddValidatedOptions<DataIsolationOptions>(DataIsolationOptions.SectionName);
        services.AddScoped<IDataIsolationContext, DataIsolationContext>();

        // Tartib muhim: ownership → soft delete → audit; domain event'lar SaveChanges'dan keyin.
        services.AddScoped<ISaveChangesInterceptor, OwnershipInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, SoftDeleteInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, AuditingInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventsInterceptor>();

        // Connection string lazy o'qiladi: testlar (WebApplicationFactory) uni keyinroq almashtira oladi.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

            options
                // SplitQuery: Post.Tags/Post.Media kabi bir nechta collection birga yuklanganda cartesian explosion bo'lmaydi.
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsHistoryTable(MigrationsHistoryTable)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
                .AddInterceptors(sp.GetRequiredService<TransactionalEmailQueue>());

            if (environment.IsDevelopment())
                options.EnableDetailedErrors();
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddConventionalRepositories(typeof(DependencyInjection).Assembly);

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }

    private static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;

                // .NET 10 sxemasi (passkey jadvali bilan); design-time factory ham shuni ishlatadi.
                options.Stores.SchemaVersion = AppDbContext.IdentitySchemaVersion;
                options.SignIn.RequireConfirmedEmail = configuration.GetValue("Auth:RequireConfirmedEmail", true);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Sxemalar (JWT) Auth moduli tomonidan qo'shiladi.
        services.AddAuthentication();
        services.AddAuthorization();

        return services;
    }

    private static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        services.AddValidatedOptions<LocalizationOptions>(LocalizationOptions.SectionName);
        services.AddSingleton<ILocalizer, JsonLocalizer>();
        ValidatorOptions.Global.LanguageManager = new UzbekAwareLanguageManager();

        services.AddSingleton<ISlugGenerator, SlugGenerator>();

        services.AddValidatedOptions<ContentOptions>(ContentOptions.SectionName);
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();

        services.AddValidatedOptions<StorageOptions>(StorageOptions.SectionName);
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());

        services.AddHybridCache();
        services.AddSingleton<ICacheService, HybridCacheService>();

        services.AddValidatedOptions<SmtpOptions>(SmtpOptions.SectionName);
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<ChannelEmailQueue>();
        // Xatlar tranzaksiya commit bo'lgandan keyingina navbatga tushadi (TransactionalEmailQueue).
        services.AddScoped<TransactionalEmailQueue>();
        services.AddScoped<IEmailQueue>(sp => sp.GetRequiredService<TransactionalEmailQueue>());
        services.AddSingleton<IEmailTemplateRenderer, EmbeddedEmailTemplateRenderer>();

        return services;
    }

    private static IServiceCollection AddBackgroundProcessing(this IServiceCollection services)
    {
        services.AddValidatedOptions<BackgroundJobsOptions>(BackgroundJobsOptions.SectionName);
        services.AddHostedService<RecurringJobRunner>();
        services.AddHostedService<EmailDispatcherHostedService>();
        return services;
    }

    internal static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

    /// <summary>
    /// Konvensiya: nomi "Repository" bilan tugaydigan (generic bo'lmagan) klasslar Application assembly'sida
    /// e'lon qilingan har bir interfeysi uchun scoped ro'yxatdan o'tadi (masalan PostRepository → IPostRepository).
    /// </summary>
    internal static IServiceCollection AddConventionalRepositories(this IServiceCollection services, Assembly assembly)
    {
        var applicationAssembly = typeof(IUnitOfWork).Assembly;

        var repositoryTypes = assembly.GetTypes().Where(t =>
            t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            && t.Name.EndsWith("Repository", StringComparison.Ordinal));

        foreach (var type in repositoryTypes)
        {
            foreach (var @interface in type.GetInterfaces().Where(i => i.Assembly == applicationAssembly))
                services.AddScoped(@interface, type);
        }

        return services;
    }
}
