using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.SocialLinks;

public sealed record AddSocialLinkCommand(SocialPlatform Platform, string Url) : ICommand<SocialLinkDto>;

public sealed record UpdateSocialLinkCommand(Guid Id, SocialPlatform Platform, string Url) : ICommand;

public sealed record RemoveSocialLinkCommand(Guid Id) : ICommand;

public sealed record ReorderSocialLinksCommand(IReadOnlyList<Guid> OrderedIds) : ICommand;

internal sealed class AddSocialLinkCommandValidator : AbstractValidator<AddSocialLinkCommand>
{
    public AddSocialLinkCommandValidator()
    {
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.Url).NotEmpty().MaximumLength(UrlMaxLength);
    }
}

internal sealed class UpdateSocialLinkCommandValidator : AbstractValidator<UpdateSocialLinkCommand>
{
    public UpdateSocialLinkCommandValidator()
    {
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.Url).NotEmpty().MaximumLength(UrlMaxLength);
    }
}

internal sealed class ReorderSocialLinksCommandValidator : AbstractValidator<ReorderSocialLinksCommand>
{
    public ReorderSocialLinksCommandValidator() => RuleFor(x => x.OrderedIds).NotNull();
}

internal sealed class AddSocialLinkCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<AddSocialLinkCommand, SocialLinkDto>(context)
{
    protected override ProfileSections Sections => ProfileSections.SocialLinks;

    protected override Task<Result<SocialLinkDto>> ApplyAsync(UserProfile profile, AddSocialLinkCommand command,
        CancellationToken cancellationToken)
    {
        var link = profile.AddSocialLink(command.Platform, command.Url);
        return Task.FromResult(link.IsSuccess
            ? Result.Success(ProfileMapper.ToDto(link.Value))
            : Result.Failure<SocialLinkDto>(link.Error));
    }
}

internal sealed class UpdateSocialLinkCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdateSocialLinkCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.SocialLinks;

    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateSocialLinkCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdateSocialLink(command.Id, command.Platform, command.Url));
}

internal sealed class RemoveSocialLinkCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<RemoveSocialLinkCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.SocialLinks;

    protected override Task<Result> ApplyAsync(UserProfile profile, RemoveSocialLinkCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.RemoveSocialLink(command.Id));
}

internal sealed class ReorderSocialLinksCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<ReorderSocialLinksCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.SocialLinks;

    protected override Task<Result> ApplyAsync(UserProfile profile, ReorderSocialLinksCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.ReorderSocialLinks(command.OrderedIds));
}
