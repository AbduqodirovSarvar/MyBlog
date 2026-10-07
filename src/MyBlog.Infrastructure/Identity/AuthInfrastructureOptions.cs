using System.ComponentModel.DataAnnotations;

namespace MyBlog.Infrastructure.Identity;

/// <summary>"Jwt" bo'limi. SigningKey production'da user-secrets yoki Jwt__SigningKey env orqali beriladi.</summary>
internal sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 kaliti, kamida 64 belgi.</summary>
    [Required, MinLength(64)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 24 * 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>"Seed:SuperAdmin" bo'limi. Parol bo'sh bo'lsa seed o'tkazib yuboriladi.</summary>
internal sealed class SuperAdminSeedOptions
{
    public const string SectionName = "Seed:SuperAdmin";

    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
