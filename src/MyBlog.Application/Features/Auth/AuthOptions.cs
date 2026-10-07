using System.ComponentModel.DataAnnotations;

namespace MyBlog.Application.Features.Auth;

/// <summary>"Auth" bo'limi.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool RequireConfirmedEmail { get; set; } = true;

    [Range(1, 24 * 30)]
    public int EmailTokenLifetimeHours { get; set; } = 24;

    [Range(5, 24 * 60)]
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 60;
}

/// <summary>"Frontend" bo'limi: email'dagi havolalar uchun. Yo'llarda "{culture}" joriy til bilan almashtiriladi.</summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ConfirmEmailPath { get; set; } = "/{culture}/auth/confirm-email";

    [Required]
    public string ResetPasswordPath { get; set; } = "/{culture}/auth/reset-password";
}
