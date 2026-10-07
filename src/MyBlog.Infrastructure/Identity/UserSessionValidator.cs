using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Identity;

/// <summary>
/// Security stamp → access token'dagi "sv" claim qiymati. Xom stamp token'ga yozilmaydi (u Identity'ning TOTP
/// provider'lari uchun kalit material), faqat SHA-256 xeshining 128 biti.
/// </summary>
internal static class SessionVersions
{
    public static string From(string? securityStamp)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty));
        return WebEncoders.Base64UrlEncode(hash, 0, 16);
    }

    public static bool AreEqual(string expected, string? actual) =>
        actual is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}

/// <summary>
/// Access token'ning sessiya versiyasini foydalanuvchining joriy holati (security stamp, bloklanganlik) bilan solishtiradi.
/// Holat qisqa muddat keshlanadi; stamp o'zgarganda <see cref="InvalidateAsync"/> keshni darhol tozalaydi.
/// </summary>
internal interface IUserSessionValidator
{
    Task<bool> IsValidAsync(Guid userId, string? sessionVersion, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Keshdagi holat. Immutable — HybridCache L1'da nusxalamasdan saqlaydi.</summary>
[ImmutableObject(true)]
internal sealed record UserSessionState(bool Exists, bool IsBlocked, string SessionVersion)
{
    public static readonly UserSessionState Missing = new(false, false, string.Empty);
}

/// <summary>Foydalanuvchining joriy sessiya holati bazadan (keshsiz).</summary>
internal interface IUserSessionStateReader
{
    Task<UserSessionState> ReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

internal sealed class UserSessionStateReader(AppDbContext dbContext) : IUserSessionStateReader
{
    public async Task<UserSessionState> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp, u.IsBlocked })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? UserSessionState.Missing
            : new UserSessionState(true, row.IsBlocked, SessionVersions.From(row.SecurityStamp));
    }
}

internal sealed class UserSessionValidator(IUserSessionStateReader reader, ICacheService cache) : IUserSessionValidator
{
    /// <summary>
    /// Bitta instansiyada invalidatsiya aniq (RemoveAsync). Bir nechta instansiyada L1 kesh shu muddatgacha eskirgan
    /// bo'lishi mumkin — ya'ni bloklangan token boshqa node'da ko'pi bilan shuncha vaqt ishlaydi.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public static string CacheKey(Guid userId) => $"auth:session:{userId:N}";

    public async Task<bool> IsValidAsync(Guid userId, string? sessionVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionVersion))
            return false;

        var cached = await cache.GetOrCreateAsync(CacheKey(userId), ct => LoadAsync(userId, ct), CacheDuration,
            cancellationToken: cancellationToken);
        if (IsValid(cached, sessionVersion))
            return true;

        // Rad etishdan oldin bazadan qayta tekshiriladi: kesh poyga tufayli eski stamp'ni ushlab qolgan bo'lsa,
        // yangi (to'g'ri) token rad etilmasin. Bu yo'l faqat yaroqsiz token'lar uchun ishlaydi.
        var fresh = await LoadAsync(userId, cancellationToken);
        if (fresh != cached)
            await cache.RemoveAsync(CacheKey(userId), cancellationToken);

        return IsValid(fresh, sessionVersion);
    }

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKey(userId), cancellationToken);

    private static bool IsValid(UserSessionState state, string sessionVersion) =>
        state.Exists && !state.IsBlocked && SessionVersions.AreEqual(state.SessionVersion, sessionVersion);

    private Task<UserSessionState> LoadAsync(Guid userId, CancellationToken cancellationToken) =>
        reader.ReadAsync(userId, cancellationToken);
}

/// <summary>JwtBearer hodisasi: imzo/muddat to'g'ri bo'lgan token'ning sessiyasi ham tekshiriladi.</summary>
internal static class UserSessionTokenValidation
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirst(JwtClaimNames.Subject)?.Value;
        var sessionVersion = principal?.FindFirst(JwtClaimNames.SessionVersion)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("The token has no valid subject.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IUserSessionValidator>();
        if (!await validator.IsValidAsync(userId, sessionVersion, context.HttpContext.RequestAborted))
            context.Fail("The session is no longer valid.");
    }
}
