using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.Login;

public sealed record LoginCommand(string EmailOrUserName, string Password) : ICommand<AuthResponse>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.EmailOrUserName)
            .NotEmpty().WithErrorCode("Auth.LoginRequired").WithMessage("Email or username is required.")
            .MaximumLength(AuthValidationRules.EmailMaxLength).WithErrorCode("Auth.InvalidCredentials")
            .WithMessage("Invalid email/username or password.");
        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode("Auth.PasswordRequired").WithMessage("Password is required.")
            .MaximumLength(AuthValidationRules.PasswordMaxLength).WithErrorCode("Auth.InvalidCredentials")
            .WithMessage("Invalid email/username or password.");
    }
}

/// <summary>
/// Xatolar: noto'g'ri login/parol (umumiy), lockout (daqiqalar bilan), bloklangan, email tasdiqlanmagan.
/// Bloklangan/tasdiqlanmagan holat faqat parol to'g'ri bo'lganda oshkor qilinadi.
/// </summary>
internal sealed class LoginCommandHandler(
    IIdentityService identityService,
    AuthSessionIssuer sessionIssuer,
    TimeProvider timeProvider) : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByEmailOrUserNameAsync(request.EmailOrUserName.Trim(), cancellationToken);
        if (user is null)
            return AuthErrors.InvalidCredentials;

        var check = await identityService.CheckPasswordAsync(user.Id, request.Password, cancellationToken);
        switch (check.Status)
        {
            case PasswordCheckStatus.InvalidPassword:
                return AuthErrors.InvalidCredentials;
            case PasswordCheckStatus.LockedOut:
                return AuthErrors.LockedOutFor(MinutesUntil(check.LockoutEnd));
            case PasswordCheckStatus.Blocked:
                return AuthErrors.UserBlocked;
            case PasswordCheckStatus.EmailNotConfirmed:
                return AuthErrors.EmailNotConfirmed;
        }

        await identityService.UpdateLastLoginAsync(user.Id, cancellationToken);
        return await sessionIssuer.IssueAsync(user, cancellationToken);
    }

    private int MinutesUntil(DateTimeOffset? lockoutEnd)
    {
        if (lockoutEnd is null)
            return 1;

        var remaining = lockoutEnd.Value - timeProvider.GetUtcNow();
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
    }
}
