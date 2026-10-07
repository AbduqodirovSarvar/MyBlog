using Microsoft.AspNetCore.Identity;

namespace MyBlog.Infrastructure.Identity;

/// <summary>
/// Identity foydalanuvchisi. Faqat autentifikatsiya ma'lumotlari; profil ma'lumotlari Domain'dagi
/// alohida aggregate'da (Id bir xil) saqlanadi.
/// </summary>
internal sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsBlocked { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

internal sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() => Id = Guid.CreateVersion7();

    public ApplicationRole(string roleName) : this() => Name = roleName;

    public string? Description { get; set; }
}

/// <summary>Rolga biriktirilgan ruxsat (Permissions.* konstantalaridan biri).</summary>
internal sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;
}

/// <summary>Refresh token. Bazada faqat hash saqlanadi; rotatsiyada eski token yangisiga bog'lanadi.</summary>
internal sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
