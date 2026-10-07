using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.ResetPassword;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword, string ConfirmPassword) : ICommand;

internal sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Token).RequiredToken();
        RuleFor(x => x.NewPassword).StrongPassword();
        RuleFor(x => x.ConfirmPassword).MatchesPassword(x => x.NewPassword);
    }
}

/// <summary>
/// Muvaffaqiyatda: security stamp yangilanadi (Identity), barcha refresh token'lar bekor qilinadi va
/// "parol o'zgardi" xati yuboriladi. Foydalanuvchi topilmasa — token xatosi (mavjudlik oshkor qilinmaydi).
/// </summary>
internal sealed class ResetPasswordCommandHandler(
    IIdentityService identityService,
    IRefreshTokenService refreshTokenService,
    AuthEmailService emailService) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is null)
            return AuthErrors.InvalidToken;

        var reset = await identityService.ResetPasswordAsync(user.Id, request.Token, request.NewPassword, cancellationToken);
        if (reset.IsFailure)
            return reset;

        await refreshTokenService.RevokeAllForUserAsync(user.Id, RefreshTokenRevokeReasons.PasswordReset, cancellationToken);
        await emailService.SendPasswordChangedAsync(user, cancellationToken);
        return Result.Success();
    }
}
