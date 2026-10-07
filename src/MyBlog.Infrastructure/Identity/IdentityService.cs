using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Auth;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Identity;

/// <summary>UserManager ustidagi yupqa qatlam: Identity tiplari Application'ga chiqmaydi.</summary>
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext,
    IOptions<IdentityOptions> identityOptions,
    ILocalizer localizer,
    TimeProvider timeProvider,
    IUserSessionValidator sessionValidator,
    DummyPasswordVerifier dummyPasswordVerifier) : IIdentityService
{
    public async Task<Result<Guid>> CreateUserAsync(string email, string userName, string password,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Email = email,
            UserName = userName,
            CreatedAt = timeProvider.GetUtcNow()
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            return MapErrors(created.Errors, passwordField: "password");

        var role = await userManager.AddToRoleAsync(user, Roles.User);
        if (!role.Succeeded)
            throw new InvalidOperationException($"Cannot assign role '{Roles.User}': {Describe(role)}. Are roles seeded?");

        return user.Id;
    }

    public async Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await ToAuthUserAsync(await userManager.FindByIdAsync(userId.ToString()), cancellationToken);

    public async Task<AuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await ToAuthUserAsync(await userManager.FindByEmailAsync(email), cancellationToken);

    public async Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default) =>
        await ToAuthUserAsync(await FindApplicationUserAsync(emailOrUserName), cancellationToken);

    public async Task<bool> IsEmailTakenAsync(string email, CancellationToken cancellationToken = default) =>
        await userManager.FindByEmailAsync(email) is not null;

    public async Task<bool> IsUserNameTakenAsync(string userName, CancellationToken cancellationToken = default) =>
        await userManager.FindByNameAsync(userName) is not null;

    public async Task<PasswordCheckResult> CheckCredentialsAsync(string emailOrUserName, string password,
        CancellationToken cancellationToken = default)
    {
        var user = await FindApplicationUserAsync(emailOrUserName);
        if (user?.PasswordHash is null)
        {
            // Mavjud foydalanuvchi bilan bir xil ish: bitta to'liq xesh tekshiruvi.
            dummyPasswordVerifier.Verify(password);
            return new PasswordCheckResult(PasswordCheckStatus.InvalidPassword);
        }

        var lockedOut = await userManager.IsLockedOutAsync(user);
        var stampBefore = user.SecurityStamp;

        // Parol har doim tekshiriladi (lockout'da ham) — javob vaqti holatga bog'liq bo'lmasin.
        var passwordValid = await userManager.CheckPasswordAsync(user, password);

        // Rehash kerak bo'lsa Identity stamp'ni ham yangilaydi — keshdagi eski qiymat yangi token'ni rad etmasin.
        if (!string.Equals(stampBefore, user.SecurityStamp, StringComparison.Ordinal))
            await sessionValidator.InvalidateAsync(user.Id, cancellationToken);

        // Lockout paytida urinishlar hisoblanmaydi va natija oshkor qilinmaydi (handler umumiy xato qaytaradi).
        if (lockedOut)
            return new PasswordCheckResult(PasswordCheckStatus.LockedOut);

        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            return new PasswordCheckResult(PasswordCheckStatus.InvalidPassword);
        }

        if (await userManager.GetAccessFailedCountAsync(user) > 0)
            await userManager.ResetAccessFailedCountAsync(user);

        // Bloklangan/tasdiqlanmagan holat faqat to'g'ri paroldan keyin aytiladi.
        if (user.IsBlocked)
            return new PasswordCheckResult(PasswordCheckStatus.Blocked);

        if (identityOptions.Value.SignIn.RequireConfirmedEmail && !user.EmailConfirmed)
            return new PasswordCheckResult(PasswordCheckStatus.EmailNotConfirmed);

        return PasswordCheckResult.Succeeded(user.Id);
    }

    public Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return dbContext.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now), cancellationToken);
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredAsync(userId);
        return IdentityTokenEncoding.Encode(await userManager.GenerateEmailConfirmationTokenAsync(user));
    }

    public async Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var decoded = IdentityTokenEncoding.Decode(token);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (decoded is null || user is null)
            return AuthErrors.InvalidToken;

        var result = await userManager.ConfirmEmailAsync(user, decoded);
        return result.Succeeded ? Result.Success() : AuthErrors.InvalidToken;
    }

    public async Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredAsync(userId);
        return IdentityTokenEncoding.Encode(await userManager.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var decoded = IdentityTokenEncoding.Decode(token);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (decoded is null || user is null)
            return AuthErrors.InvalidToken;

        // ResetPasswordAsync security stamp'ni ham yangilaydi.
        var result = await userManager.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? AuthErrors.InvalidToken
                : MapErrors(result.Errors, passwordField: "newPassword");
        }

        await sessionValidator.InvalidateAsync(user.Id, cancellationToken);

        // Parol tiklangach lockout bekor qilinadi.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredAsync(userId);

        // ChangePasswordAsync security stamp'ni ham yangilaydi.
        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (result.Succeeded)
        {
            await sessionValidator.InvalidateAsync(user.Id, cancellationToken);
            return Result.Success();
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            // Token o'g'irlangan bo'lsa parolni terib ko'rishni qiyinlashtiradi.
            await userManager.AccessFailedAsync(user);
            return FieldError("currentPassword", AuthErrors.CurrentPasswordInvalid);
        }

        return MapErrors(result.Errors, passwordField: "newPassword");
    }

    public async Task<Result> SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound;

        user.IsBlocked = blocked;
        user.BlockedAt = blocked ? timeProvider.GetUtcNow() : null;

        // Security stamp yangilanadi va o'zgarishlar shu bilan saqlanadi.
        return await UpdateSecurityStampAsync(user, cancellationToken);
    }

    public async Task<Result> InvalidateSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound;

        return await UpdateSecurityStampAsync(user, cancellationToken);
    }

    public async Task<Result> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound;
        if (!await RoleExistsAsync(role, cancellationToken))
            return AuthErrors.RoleNotFound;
        if (await userManager.IsInRoleAsync(user, role))
            return Result.Success();

        var result = await userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException(Describe(result));

        // Identity rol o'zgarishida stamp'ni yangilamaydi — token'dagi rollar eskirgani uchun qo'lda yangilanadi.
        return await UpdateSecurityStampAsync(user, cancellationToken);
    }

    public async Task<Result> RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound;
        if (!await userManager.IsInRoleAsync(user, role))
            return Result.Success();

        var result = await userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException(Describe(result));

        return await UpdateSecurityStampAsync(user, cancellationToken);
    }

    public Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default)
    {
        var normalized = userManager.NormalizeName(role);
        return dbContext.UserRoles.CountAsync(
            ur => dbContext.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalized), cancellationToken);
    }

    public async Task<UserSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .ToListAsync(cancellationToken);

        return (await ToSummariesAsync(users, cancellationToken)).SingleOrDefault();
    }

    public async Task<PagedList<UserSummary>> ListUsersAsync(UserListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedEmail!.Contains(pattern) || u.NormalizedUserName!.Contains(pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var normalizedRole = userManager.NormalizeName(filter.Role);
            query = query.Where(u => dbContext.UserRoles.Any(ur =>
                ur.UserId == u.Id && dbContext.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizedRole)));
        }

        if (filter.IsBlocked is { } isBlocked)
            query = query.Where(u => u.IsBlocked == isBlocked);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedList<UserSummary>.Empty(filter.Page, filter.PageSize);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<UserSummary>(await ToSummariesAsync(users, cancellationToken), filter.Page, filter.PageSize, total);
    }

    // ---------- Yordamchi metodlar ----------

    private async Task<ApplicationUser?> FindApplicationUserAsync(string emailOrUserName) =>
        emailOrUserName.Contains('@', StringComparison.Ordinal)
            ? await userManager.FindByEmailAsync(emailOrUserName)
            : await userManager.FindByNameAsync(emailOrUserName);

    /// <summary>Stamp'ni yangilaydi (o'zgarishlar shu bilan saqlanadi) va sessiya keshini tozalaydi.</summary>
    private async Task<Result> UpdateSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(Describe(result));

        await sessionValidator.InvalidateAsync(user.Id, cancellationToken);
        return Result.Success();
    }

    private async Task<ApplicationUser> GetRequiredAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString())
        ?? throw new InvalidOperationException($"User '{userId}' was not found.");

    private async Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken)
    {
        var normalized = userManager.NormalizeName(role);
        return await dbContext.Roles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken);
    }

    private async Task<AuthUser?> ToAuthUserAsync(ApplicationUser? user, CancellationToken cancellationToken)
    {
        if (user is null)
            return null;

        var roles = (await userManager.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();

        var permissions = await (
                from userRole in dbContext.UserRoles
                join permission in dbContext.RolePermissions on userRole.RoleId equals permission.RoleId
                where userRole.UserId == user.Id
                select permission.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);
        permissions.Sort(StringComparer.Ordinal);

        return new AuthUser(user.Id, user.Email ?? string.Empty, user.UserName ?? string.Empty, user.EmailConfirmed,
            user.IsBlocked, roles, permissions, SessionVersions.From(user.SecurityStamp));
    }

    private async Task<List<UserSummary>> ToSummariesAsync(List<ApplicationUser> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
            return [];

        var ids = users.Select(u => u.Id).ToList();

        var roles = await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);
        var rolesByUser = roles.ToLookup(r => r.UserId, r => r.Name!);

        // Admin boshqa foydalanuvchilarning profilini ko'radi — ownership filtri o'chiriladi.
        var displayNames = await dbContext.Set<UserProfile>()
            .IgnoreQueryFilters([QueryFilterNames.Ownership])
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName })
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);

        return users.Select(u => new UserSummary(
                u.Id,
                u.Email ?? string.Empty,
                u.UserName ?? string.Empty,
                displayNames.GetValueOrDefault(u.Id),
                u.EmailConfirmed,
                u.IsBlocked,
                u.BlockedAt,
                u.LockoutEnd,
                u.CreatedAt,
                u.LastLoginAt,
                rolesByUser[u.Id].Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>Identity xatolari → Application xatolari (parol xatolari maydon bo'yicha validatsiya xatosi).</summary>
    private Error MapErrors(IEnumerable<IdentityError> errors, string passwordField)
    {
        var list = errors.ToList();

        if (list.Exists(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
            return AuthErrors.EmailTaken;
        if (list.Exists(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return AuthErrors.UsernameTaken;
        if (list.Exists(e => e.Code == nameof(IdentityErrorDescriber.InvalidUserName)))
            return FieldError("userName", Error.Validation("Auth.UsernameInvalid", "Username is not valid."));
        if (list.Exists(e => e.Code == nameof(IdentityErrorDescriber.InvalidEmail)))
            return FieldError("email", Error.Validation("Auth.EmailInvalid", "Email address is not valid."));

        return FieldError(passwordField, AuthErrors.PasswordPolicy);
    }

    private ValidationError FieldError(string field, Error error) =>
        new([new FieldError(field, error.Code, localizer.Get(error.Code, error.Description))]);

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
