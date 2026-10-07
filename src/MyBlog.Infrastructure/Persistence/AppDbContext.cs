using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Identity;

namespace MyBlog.Infrastructure.Persistence;

/// <summary>
/// Yagona DbContext. Entity konfiguratsiyalari assembly'dan avtomatik olinadi
/// (Persistence/Configurations, Identity/Configurations). Modul DbSet'lari shart emas: Set&lt;T&gt;() ishlatiladi.
/// </summary>
internal sealed class AppDbContext(DbContextOptions<AppDbContext> options, IDataIsolationContext isolation)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IUnitOfWork, IDataFilterSource
{
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Query filter'lar shu property'larni o'qiydi; EF ularni har bir so'rovda qayta baholaydi.
    public bool IsolationEnabled => isolation.IsEnabled;
    public bool BypassIsolation => isolation.Bypass;
    public Guid CurrentUserId => isolation.CurrentUserId ?? Guid.Empty;

    /// <summary>
    /// Identity sxema versiyasi (.NET 10, passkey jadvali bilan). IdentityDbContext uni DI'dagi IdentityOptions'dan
    /// o'qiydi, shuning uchun runtime (AddIdentityCore) va design-time factory ikkalasi ham shu qiymatni beradi.
    /// </summary>
    internal static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version3;

    protected override Version SchemaVersion => IdentitySchemaVersion;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        builder.ApplyDataFilters(this);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Ichma-ich chaqiruv: tashqi tranzaksiya ishlatiladi.
        if (Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        // Execution strategy: kelajakda EnableRetryOnFailure yoqilsa ham to'g'ri ishlaydi.
        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            operation,
            async (_, op, ct) =>
            {
                await using var transaction = await Database.BeginTransactionAsync(ct);

                var result = await op(ct);

                // Muvaffaqiyatsiz Result qaytsa o'zgarishlar bekor qilinadi.
                if (result is Result { IsFailure: true })
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
