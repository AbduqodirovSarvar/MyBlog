using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.Experiences;

public sealed record AddExperienceCommand(
    string Company,
    string Position,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description) : ICommand<ExperienceDto>;

public sealed record UpdateExperienceCommand(
    Guid Id,
    string Company,
    string Position,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description) : ICommand;

public sealed record RemoveExperienceCommand(Guid Id) : ICommand;

internal sealed class AddExperienceCommandValidator : AbstractValidator<AddExperienceCommand>
{
    public AddExperienceCommandValidator()
    {
        RuleFor(x => x.Company).NotEmpty().MaximumLength(CompanyMaxLength);
        RuleFor(x => x.Position).NotEmpty().MaximumLength(PositionMaxLength);
        RuleFor(x => x.Location).MaximumLength(LocationMaxLength);
        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);
    }
}

internal sealed class UpdateExperienceCommandValidator : AbstractValidator<UpdateExperienceCommand>
{
    public UpdateExperienceCommandValidator()
    {
        RuleFor(x => x.Company).NotEmpty().MaximumLength(CompanyMaxLength);
        RuleFor(x => x.Position).NotEmpty().MaximumLength(PositionMaxLength);
        RuleFor(x => x.Location).MaximumLength(LocationMaxLength);
        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);
    }
}

internal sealed class AddExperienceCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<AddExperienceCommand, ExperienceDto>(context)
{
    protected override ProfileSections Sections => ProfileSections.Experiences;

    protected override Task<Result<ExperienceDto>> ApplyAsync(UserProfile profile, AddExperienceCommand c,
        CancellationToken cancellationToken)
    {
        var experience = profile.AddExperience(c.Company, c.Position, c.Location, c.StartDate, c.EndDate, c.IsCurrent,
            c.Description);
        return Task.FromResult(experience.IsSuccess
            ? Result.Success(ProfileMapper.ToDto(experience.Value))
            : Result.Failure<ExperienceDto>(experience.Error));
    }
}

internal sealed class UpdateExperienceCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateExperienceCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Experiences;

    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateExperienceCommand c,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateExperience(c.Id, c.Company, c.Position, c.Location, c.StartDate, c.EndDate,
            c.IsCurrent, c.Description));
}

internal sealed class RemoveExperienceCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<RemoveExperienceCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Experiences;

    protected override Task<Result> ApplyAsync(UserProfile profile, RemoveExperienceCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.RemoveExperience(command.Id));
}
