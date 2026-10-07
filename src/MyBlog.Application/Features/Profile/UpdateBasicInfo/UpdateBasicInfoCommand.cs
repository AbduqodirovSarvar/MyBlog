using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.UpdateBasicInfo;

public sealed record UpdateBasicInfoCommand(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Bio,
    DateOnly? BirthDate,
    string? Location,
    string? Profession,
    string? Company) : ICommand;

internal sealed class UpdateBasicInfoCommandValidator : AbstractValidator<UpdateBasicInfoCommand>
{
    public UpdateBasicInfoCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.FirstName).MaximumLength(FirstNameMaxLength);
        RuleFor(x => x.LastName).MaximumLength(LastNameMaxLength);
        RuleFor(x => x.DisplayName).MaximumLength(DisplayNameMaxLength);
        RuleFor(x => x.Bio).MaximumLength(BioMaxLength);
        RuleFor(x => x.Location).MaximumLength(LocationMaxLength);
        RuleFor(x => x.Profession).MaximumLength(ProfessionMaxLength);
        RuleFor(x => x.Company).MaximumLength(CompanyMaxLength);
        RuleFor(x => x.BirthDate)
            .Must(date => date is null || date <= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithErrorCode("Profile.BirthDateInFuture")
            .WithMessage("Birth date cannot be in the future.");
    }
}

internal sealed class UpdateBasicInfoCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateBasicInfoCommand>(context)
{
    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateBasicInfoCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateBasicInfo(command.FirstName, command.LastName, command.DisplayName, command.Bio,
            command.BirthDate, command.Location, command.Profession, command.Company));
}
