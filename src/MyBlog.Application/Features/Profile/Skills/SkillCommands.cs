using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.Skills;

/// <param name="Level">Ixtiyoriy; berilsa 1..100.</param>
public sealed record AddSkillCommand(string Name, int? Level) : ICommand<SkillDto>;

public sealed record UpdateSkillCommand(Guid Id, string Name, int? Level) : ICommand;

public sealed record RemoveSkillCommand(Guid Id) : ICommand;

public sealed record ReorderSkillsCommand(IReadOnlyList<Guid> OrderedIds) : ICommand;

internal sealed class AddSkillCommandValidator : AbstractValidator<AddSkillCommand>
{
    public AddSkillCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(SkillNameMaxLength);
        RuleFor(x => x.Level).InclusiveBetween(SkillLevelMin, SkillLevelMax).When(x => x.Level is not null);
    }
}

internal sealed class UpdateSkillCommandValidator : AbstractValidator<UpdateSkillCommand>
{
    public UpdateSkillCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(SkillNameMaxLength);
        RuleFor(x => x.Level).InclusiveBetween(SkillLevelMin, SkillLevelMax).When(x => x.Level is not null);
    }
}

internal sealed class ReorderSkillsCommandValidator : AbstractValidator<ReorderSkillsCommand>
{
    public ReorderSkillsCommandValidator() => RuleFor(x => x.OrderedIds).NotNull();
}

internal sealed class AddSkillCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<AddSkillCommand, SkillDto>(context)
{
    protected override ProfileSections Sections => ProfileSections.Skills;

    protected override Task<Result<SkillDto>> ApplyAsync(UserProfile profile, AddSkillCommand command,
        CancellationToken cancellationToken)
    {
        var skill = profile.AddSkill(command.Name, command.Level);
        return Task.FromResult(skill.IsSuccess
            ? Result.Success(ProfileMapper.ToDto(skill.Value))
            : Result.Failure<SkillDto>(skill.Error));
    }
}

internal sealed class UpdateSkillCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateSkillCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Skills;

    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateSkillCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateSkill(command.Id, command.Name, command.Level));
}

internal sealed class RemoveSkillCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<RemoveSkillCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Skills;

    protected override Task<Result> ApplyAsync(UserProfile profile, RemoveSkillCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.RemoveSkill(command.Id));
}

internal sealed class ReorderSkillsCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<ReorderSkillsCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Skills;

    protected override Task<Result> ApplyAsync(UserProfile profile, ReorderSkillsCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.ReorderSkills(command.OrderedIds));
}
