using System.Text.RegularExpressions;
using FluentValidation;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Auth.Common;

/// <summary>Auth validator'larida takrorlanadigan qoidalar. Parol qoidalari Identity sozlamalari bilan bir xil.</summary>
internal static partial class AuthValidationRules
{
    public const int EmailMaxLength = 256;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    [GeneratedRegex("^[a-z0-9_.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("Auth.EmailRequired").WithMessage("Email is required.")
            .MaximumLength(EmailMaxLength).WithErrorCode("Auth.EmailTooLong").WithMessage("Email is too long.")
            .Must(DomainRules.IsValidEmail).WithErrorCode("Auth.EmailInvalid").WithMessage("Email address is not valid.");

    public static IRuleBuilderOptions<T, string> ValidUsername<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("Auth.UsernameRequired").WithMessage("Username is required.")
            .Must(u => u.Length is >= UserProfileConstraints.UsernameMinLength and <= UserProfileConstraints.UsernameMaxLength
                       && UsernameRegex().IsMatch(u)
                       && UserProfile.IsValidUsername(u))
            .WithErrorCode("Auth.UsernameInvalid")
            .WithMessage("Username must be 3-32 characters long and contain only lowercase Latin letters, digits, '_' or '.'.");

    /// <summary>Yangi parol: kamida 8 belgi, raqam, kichik va katta harf.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("Auth.PasswordRequired").WithMessage("Password is required.")
            .MinimumLength(PasswordMinLength).WithErrorCode("Auth.PasswordTooShort").WithMessage("Password must be at least 8 characters long.")
            .MaximumLength(PasswordMaxLength).WithErrorCode("Auth.PasswordTooLong").WithMessage("Password must not exceed 128 characters.")
            .Must(p => p.Any(char.IsDigit)).WithErrorCode("Auth.PasswordRequiresDigit").WithMessage("Password must contain at least one digit.")
            .Must(p => p.Any(char.IsLower)).WithErrorCode("Auth.PasswordRequiresLower").WithMessage("Password must contain at least one lowercase letter.")
            .Must(p => p.Any(char.IsUpper)).WithErrorCode("Auth.PasswordRequiresUpper").WithMessage("Password must contain at least one uppercase letter.");

    public static IRuleBuilderOptions<T, string> MatchesPassword<T>(this IRuleBuilder<T, string> rule, Func<T, string> password) =>
        rule
            .Must((model, confirm) => string.Equals(confirm, password(model), StringComparison.Ordinal))
            .WithErrorCode("Auth.PasswordsDoNotMatch").WithMessage("Passwords do not match.");

    public static IRuleBuilderOptions<T, string> RequiredToken<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("Auth.TokenRequired").WithMessage("Token is required.")
            .MaximumLength(4096).WithErrorCode("Auth.InvalidToken").WithMessage("The link is invalid or has expired.");
}
