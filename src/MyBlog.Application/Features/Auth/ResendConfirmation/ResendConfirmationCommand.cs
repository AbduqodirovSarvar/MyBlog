using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.ResendConfirmation;

public sealed record ResendConfirmationCommand(string Email) : ICommand<MessageResponse>;

internal sealed class ResendConfirmationCommandValidator : AbstractValidator<ResendConfirmationCommand>
{
    public ResendConfirmationCommandValidator() => RuleFor(x => x.Email).ValidEmail();
}

/// <summary>Foydalanuvchi bor-yo'qligidan qat'i nazar bir xil javob (user enumeration'dan himoya).</summary>
internal sealed class ResendConfirmationCommandHandler(
    IIdentityService identityService,
    AuthEmailService emailService,
    ILocalizer localizer) : ICommandHandler<ResendConfirmationCommand, MessageResponse>
{
    public const string MessageKey = "Auth.ConfirmationResent";

    public async Task<Result<MessageResponse>> Handle(ResendConfirmationCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is { EmailConfirmed: false, IsBlocked: false })
            await emailService.SendEmailConfirmationAsync(user, cancellationToken);

        return new MessageResponse(localizer.Get(MessageKey,
            "If an unconfirmed account with this email exists, we have sent a new confirmation link."));
    }
}
