using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth;

/// <summary>Auth moduli xatolari (kalitlari auth.*.json'da).</summary>
public static class AuthErrors
{
    // Kirish: qaysi qism noto'g'ri ekanini oshkor qilmaymiz.
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "Invalid email/username or password.");

    public static readonly Error LockedOut =
        Error.Forbidden("Auth.LockedOut", "Too many failed attempts. Try again in {0} minute(s).");

    public static readonly Error EmailNotConfirmed =
        Error.Forbidden("Auth.EmailNotConfirmed", "Please confirm your email address before signing in.");

    public static readonly Error UserBlocked =
        Error.Forbidden("Auth.UserBlocked", "Your account has been blocked. Please contact the administrator.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.InvalidRefreshToken", "The session has expired. Please sign in again.");

    public static readonly Error SessionInvalid =
        Error.Unauthorized("Auth.SessionInvalid", "Your session is no longer valid. Please sign in again.");

    public static readonly Error InvalidToken =
        Error.Validation("Auth.InvalidToken", "The link is invalid or has expired.");

    public static readonly Error EmailTaken = Error.Conflict("Auth.EmailTaken", "This email address is already registered.");
    public static readonly Error UsernameTaken = Error.Conflict("Auth.UsernameTaken", "This username is already taken.");

    public static readonly Error PasswordPolicy =
        Error.Validation("Auth.PasswordPolicy", "The password does not meet the security requirements.");

    public static readonly Error CurrentPasswordInvalid =
        Error.Validation("Auth.CurrentPasswordInvalid", "The current password is incorrect.");

    // Admin
    public static readonly Error UserNotFound = Error.NotFound("Auth.UserNotFound", "User was not found.");
    public static readonly Error CannotBlockSelf = Error.Validation("Auth.CannotBlockSelf", "You cannot block your own account.");

    public static readonly Error CannotBlockSuperAdmin =
        Error.Forbidden("Auth.CannotBlockSuperAdmin", "A super administrator cannot be blocked.");

    public static readonly Error CannotBlockAdmin =
        Error.Forbidden("Auth.CannotBlockAdmin", "Only a super administrator can block an administrator.");

    public static readonly Error RoleNotFound = Error.Validation("Auth.RoleNotFound", "The role does not exist.");

    public static readonly Error CannotRemoveLastSuperAdmin =
        Error.Conflict("Auth.CannotRemoveLastSuperAdmin", "The last super administrator cannot lose this role.");

    public static Error LockedOutFor(int minutes) => LockedOut.WithArgs(minutes);
}
