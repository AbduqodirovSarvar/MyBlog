using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string ConfirmPassword)
    : ICommand<AuthResponse>;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithErrorCode("Auth.CurrentPasswordRequired").WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).StrongPassword();
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword, StringComparer.Ordinal)
            .WithErrorCode("Auth.NewPasswordSameAsCurrent").WithMessage("The new password must differ from the current one.");
        RuleFor(x => x.ConfirmPassword).MatchesPassword(x => x.NewPassword);
    }
}

/// <summary>
/// Parol o'zgargach barcha sessiyalar bekor qilinadi (security stamp ham yangilanadi — eski access token'lar ishlamaydi)
/// va joriy qurilma uchun yangi juftlik qaytadi.
/// </summary>
internal sealed class ChangePasswordCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    IRefreshTokenService refreshTokenService,
    AuthSessionIssuer sessionIssuer,
    AuthEmailService emailService) : ICommandHandler<ChangePasswordCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(currentUser.RequiredId, cancellationToken);
        if (user is null || user.IsBlocked)
            return AuthErrors.SessionInvalid;

        var changed = await identityService.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword,
            cancellationToken);
        if (changed.IsFailure)
            return changed.Error;

        await refreshTokenService.RevokeAllForUserAsync(user.Id, RefreshTokenRevokeReasons.PasswordChanged, cancellationToken);
        await emailService.SendPasswordChangedAsync(user, cancellationToken);

        // Security stamp o'zgardi: yangi token yangilangan stamp bilan chiqishi uchun foydalanuvchi qayta o'qiladi.
        var refreshed = await identityService.FindByIdAsync(user.Id, cancellationToken);
        if (refreshed is null)
            return AuthErrors.SessionInvalid;

        return await sessionIssuer.IssueAsync(refreshed, cancellationToken);
    }
}
