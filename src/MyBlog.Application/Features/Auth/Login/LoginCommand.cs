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
/// Xatolar: noto'g'ri login/parol (umumiy), bloklangan, email tasdiqlanmagan. Parol noto'g'ri bo'lsa holatdan qat'i nazar
/// faqat umumiy xato qaytadi; bloklangan/tasdiqlanmagan holat faqat to'g'ri paroldan keyin aytiladi.
/// Lockout ham umumiy xato bilan javob beradi: aks holda lockout paytida "to'g'ri parol" signali parol terishni
/// cheklovsiz davom ettirishga imkon beradi va mavjud akkauntlarni aniqlash mumkin bo'ladi.
/// </summary>
internal sealed class LoginCommandHandler(
    IIdentityService identityService,
    AuthSessionIssuer sessionIssuer) : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var check = await identityService.CheckCredentialsAsync(request.EmailOrUserName.Trim(), request.Password,
            cancellationToken);
        switch (check.Status)
        {
            case PasswordCheckStatus.Success when check.UserId is not null:
                break;
            case PasswordCheckStatus.Blocked:
                return AuthErrors.UserBlocked;
            case PasswordCheckStatus.EmailNotConfirmed:
                return AuthErrors.EmailNotConfirmed;
            default:
                return AuthErrors.InvalidCredentials;
        }

        // Rollar/ruxsatlar va security stamp tekshiruvdan keyin o'qiladi (token eng so'nggi stamp bilan chiqadi).
        var user = await identityService.FindByIdAsync(check.UserId.Value, cancellationToken);
        if (user is null)
            return AuthErrors.InvalidCredentials;

        await identityService.UpdateLastLoginAsync(user.Id, cancellationToken);
        return await sessionIssuer.IssueAsync(user, cancellationToken);
    }
}
