using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Auth.Register;

public sealed record RegisterCommand(
    string Email,
    string UserName,
    string Password,
    string ConfirmPassword,
    string? FirstName = null,
    string? LastName = null,
    string? Culture = null) : ICommand<RegisterResponse>;

public sealed record RegisterResponse(Guid UserId, bool RequiresEmailConfirmation);

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.UserName).ValidUsername();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.ConfirmPassword).MatchesPassword(x => x.Password);

        RuleFor(x => x.FirstName)
            .MaximumLength(UserProfileConstraints.FirstNameMaxLength)
            .WithErrorCode("Auth.FirstNameTooLong").WithMessage("First name is too long.");
        RuleFor(x => x.LastName)
            .MaximumLength(UserProfileConstraints.LastNameMaxLength)
            .WithErrorCode("Auth.LastNameTooLong").WithMessage("Last name is too long.");
        RuleFor(x => x.Culture)
            .Must(Cultures.IsSupported).When(x => x.Culture is not null)
            .WithErrorCode("Auth.CultureNotSupported").WithMessage("The language is not supported.");
    }
}
