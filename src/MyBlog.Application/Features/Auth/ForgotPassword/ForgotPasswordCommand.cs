using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : ICommand<MessageResponse>;

internal sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).ValidEmail();
}

/// <summary>
/// Har doim bir xil javob. Xat faqat foydalanuvchi mavjud, email tasdiqlangan va bloklanmagan bo'lsa yuboriladi
/// (foydalanuvchi profilidagi tilda).
/// </summary>
internal sealed class ForgotPasswordCommandHandler(
    IIdentityService identityService,
    AuthEmailService emailService,
    ILocalizer localizer) : ICommandHandler<ForgotPasswordCommand, MessageResponse>
{
    public const string MessageKey = "Auth.ForgotPasswordSent";

    public async Task<Result<MessageResponse>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is { EmailConfirmed: true, IsBlocked: false })
            await emailService.SendPasswordResetAsync(user, cancellationToken);

        return new MessageResponse(localizer.Get(MessageKey,
            "If an account with this email exists, we have sent password reset instructions to it."));
    }
}
