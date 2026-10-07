using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.UpdateContactInfo;

public sealed record UpdateContactInfoCommand(string? Website, string? PublicEmail, string? Phone) : ICommand;

internal sealed class UpdateContactInfoCommandValidator : AbstractValidator<UpdateContactInfoCommand>
{
    public UpdateContactInfoCommandValidator()
    {
        RuleFor(x => x.Website)
            .Must(url => DomainRules.TrimToNull(url) is not { } value
                         || (value.Length <= UrlMaxLength && DomainRules.IsValidHttpUrl(value)))
            .WithErrorCode(UserProfileErrors.WebsiteInvalid.Code)
            .WithMessage(UserProfileErrors.WebsiteInvalid.Description);

        RuleFor(x => x.PublicEmail)
            .Must(email => DomainRules.TrimToNull(email) is not { } value
                           || (value.Length <= EmailMaxLength && DomainRules.IsValidEmail(value)))
            .WithErrorCode(UserProfileErrors.PublicEmailInvalid.Code)
            .WithMessage(UserProfileErrors.PublicEmailInvalid.Description);

        RuleFor(x => x.Phone).MaximumLength(PhoneMaxLength);
    }
}

internal sealed class UpdateContactInfoCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateContactInfoCommand>(context)
{
    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateContactInfoCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateContactInfo(command.Website, command.PublicEmail, command.Phone));
}
