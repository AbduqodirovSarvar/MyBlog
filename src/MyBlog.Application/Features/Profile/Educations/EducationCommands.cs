using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.Educations;

public sealed record AddEducationCommand(
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description) : ICommand<EducationDto>;

public sealed record UpdateEducationCommand(
    Guid Id,
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description) : ICommand;

public sealed record RemoveEducationCommand(Guid Id) : ICommand;

internal sealed class AddEducationCommandValidator : AbstractValidator<AddEducationCommand>
{
    public AddEducationCommandValidator()
    {
        RuleFor(x => x.Institution).NotEmpty().MaximumLength(InstitutionMaxLength);
        RuleFor(x => x.Degree).MaximumLength(DegreeMaxLength);
        RuleFor(x => x.FieldOfStudy).MaximumLength(FieldOfStudyMaxLength);
        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);
    }
}

internal sealed class UpdateEducationCommandValidator : AbstractValidator<UpdateEducationCommand>
{
    public UpdateEducationCommandValidator()
    {
        RuleFor(x => x.Institution).NotEmpty().MaximumLength(InstitutionMaxLength);
        RuleFor(x => x.Degree).MaximumLength(DegreeMaxLength);
        RuleFor(x => x.FieldOfStudy).MaximumLength(FieldOfStudyMaxLength);
        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);
    }
}

internal sealed class AddEducationCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<AddEducationCommand, EducationDto>(context)
{
    protected override ProfileSections Sections => ProfileSections.Educations;

    protected override Task<Result<EducationDto>> ApplyAsync(UserProfile profile, AddEducationCommand c,
        CancellationToken cancellationToken)
    {
        var education = profile.AddEducation(c.Institution, c.Degree, c.FieldOfStudy, c.StartDate, c.EndDate, c.Description);
        return Task.FromResult(education.IsSuccess
            ? Result.Success(ProfileMapper.ToDto(education.Value))
            : Result.Failure<EducationDto>(education.Error));
    }
}

internal sealed class UpdateEducationCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateEducationCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Educations;

    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateEducationCommand c,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateEducation(c.Id, c.Institution, c.Degree, c.FieldOfStudy, c.StartDate, c.EndDate,
            c.Description));
}

internal sealed class RemoveEducationCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<RemoveEducationCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Educations;

    protected override Task<Result> ApplyAsync(UserProfile profile, RemoveEducationCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.RemoveEducation(command.Id));
}
