using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Identity.Seeding;

/// <summary>
/// Tizim rollari (SuperAdmin, Admin, User) va ularning default ruxsatlari. Idempotent: yetishmayotgan rol va
/// ruxsatlar qo'shiladi, <see cref="Permissions.All"/>'da yo'q (eskirgan) ruxsatlar o'chiriladi.
/// </summary>
internal sealed class RolesAndPermissionsSeeder(
    RoleManager<ApplicationRole> roleManager,
    AppDbContext dbContext,
    ILogger<RolesAndPermissionsSeeder> logger) : IDataSeeder
{
    public int Order => 0;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in Roles.All)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new ApplicationRole(roleName);
                var created = await roleManager.CreateAsync(role);
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        $"Cannot create role '{roleName}': {string.Join("; ", created.Errors.Select(e => e.Description))}");
                logger.LogInformation("Role {Role} created", roleName);
            }

            await SyncPermissionsAsync(role, Permissions.DefaultsByRole[roleName], cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncPermissionsAsync(ApplicationRole role, IReadOnlyList<string> defaults, CancellationToken cancellationToken)
    {
        var existing = await dbContext.RolePermissions
            .Where(p => p.RoleId == role.Id)
            .ToListAsync(cancellationToken);
        var existingNames = existing.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);

        var missing = defaults.Where(p => !existingNames.Contains(p)).ToList();
        dbContext.RolePermissions.AddRange(missing.Select(p => new RolePermission { RoleId = role.Id, Permission = p }));

        var stale = existing.Where(p => !Permissions.All.Contains(p.Permission)).ToList();
        dbContext.RolePermissions.RemoveRange(stale);

        if (missing.Count > 0 || stale.Count > 0)
            logger.LogInformation("Role {Role}: {Added} permission(s) added, {Removed} stale permission(s) removed",
                role.Name, missing.Count, stale.Count);
    }
}

/// <summary>
/// "Seed:SuperAdmin" bo'yicha tasdiqlangan SuperAdmin foydalanuvchi va uning profilini yaratadi.
/// Email yoki username band bo'lsa — o'tkazib yuboriladi; parol bo'sh bo'lsa ogohlantirish bilan o'tkaziladi.
/// </summary>
internal sealed class SuperAdminSeeder(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext,
    IOptions<SuperAdminSeedOptions> options,
    TimeProvider timeProvider,
    ILogger<SuperAdminSeeder> logger) : IDataSeeder
{
    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;

        if (string.IsNullOrWhiteSpace(seed.Password))
        {
            logger.LogWarning("Seed:SuperAdmin:Password is empty; super administrator is not seeded");
            return;
        }

        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.UserName))
        {
            logger.LogWarning("Seed:SuperAdmin:Email or UserName is empty; super administrator is not seeded");
            return;
        }

        var email = seed.Email.Trim();
        var userName = seed.UserName.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null || await userManager.FindByNameAsync(userName) is not null)
        {
            logger.LogDebug("Super administrator {UserName} already exists", userName);
            return;
        }

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var user = new ApplicationUser
            {
                Email = email,
                UserName = userName,
                EmailConfirmed = true,
                CreatedAt = timeProvider.GetUtcNow()
            };

            EnsureSucceeded(await userManager.CreateAsync(user, seed.Password), "create super administrator");
            EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.SuperAdmin), "assign SuperAdmin role");

            var profile = UserProfile.Create(user.Id, userName);
            if (profile.IsFailure)
                throw new InvalidOperationException($"Cannot create super administrator profile: {profile.Error.Description}");

            dbContext.Add(profile.Value);
            await dbContext.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

        logger.LogInformation("Super administrator {UserName} created", userName);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Cannot {action}: {string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))}");
    }
}
