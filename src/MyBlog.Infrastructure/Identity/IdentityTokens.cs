using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyBlog.Infrastructure.Identity;

/// <summary>Identity tokenlari URL'da xavfsiz bo'lishi uchun Base64Url ko'rinishida tashqariga chiqadi.</summary>
internal static class IdentityTokenEncoding
{
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>Noto'g'ri formatda bo'lsa null.</summary>
    public static string? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Refresh token: 64 bayt tasodifiy qiymat (Base64Url); bazada faqat SHA-256 hash (hex).</summary>
internal static class RefreshTokenCrypto
{
    public const int TokenBytes = 64;

    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal enum RefreshTokenState
{
    Active = 0,
    NotFound = 1,
    Expired = 2,
    Revoked = 3,

    /// <summary>Rotatsiya qilingan (almashtirilgan) token yana ishlatildi — o'g'irlangan bo'lishi mumkin.</summary>
    Reused = 4
}

internal static class RefreshTokenPolicy
{
    public static RefreshTokenState Evaluate(RefreshToken? token, DateTimeOffset now) => token switch
    {
        null => RefreshTokenState.NotFound,
        { RevokedAt: not null, ReplacedByTokenHash: not null } => RefreshTokenState.Reused,
        { RevokedAt: not null } => RefreshTokenState.Revoked,
        _ when token.ExpiresAt <= now => RefreshTokenState.Expired,
        _ => RefreshTokenState.Active
    };
}

/// <summary>Parolni tiklash tokeni uchun alohida provider — muddati email tokenidan mustaqil sozlanadi.</summary>
internal sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "PasswordReset";

    public PasswordResetTokenProviderOptions() => Name = "PasswordResetDataProtectorTokenProvider";
}

internal sealed class PasswordResetTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);
