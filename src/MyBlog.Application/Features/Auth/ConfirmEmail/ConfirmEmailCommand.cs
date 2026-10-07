using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.ConfirmEmail;

public sealed record ConfirmEmailCommand(Guid UserId, string Token) : ICommand;

internal sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("Auth.UserIdRequired").WithMessage("User id is required.");
        RuleFor(x => x.Token).RequiredToken();
    }
}

/// <summary>Tasdiqlangandan keyin "welcome" xati yuboriladi. Qayta tasdiqlash — muvaffaqiyat (idempotent).</summary>
internal sealed class ConfirmEmailCommandHandler(IIdentityService identityService, AuthEmailService emailService)
    : ICommandHandler<ConfirmEmailCommand>
{
    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return AuthErrors.InvalidToken;

        if (user.EmailConfirmed)
            return Result.Success();

        var confirmed = await identityService.ConfirmEmailAsync(user.Id, request.Token, cancellationToken);
        if (confirmed.IsFailure)
            return confirmed;

        await emailService.SendWelcomeAsync(user, cancellationToken);
        return Result.Success();
    }
}
