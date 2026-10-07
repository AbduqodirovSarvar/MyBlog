using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.UpdatePreferences;

public sealed record UpdatePreferencesCommand(string PreferredCulture, bool NotifyOnComment, bool NotifyOnReply) : ICommand;

internal sealed class UpdatePreferencesCommandValidator : AbstractValidator<UpdatePreferencesCommand>
{
    public UpdatePreferencesCommandValidator() =>
        RuleFor(x => x.PreferredCulture)
            .Must(Cultures.IsSupported)
            .WithErrorCode("Profile.CultureNotSupported")
            .WithMessage("This language is not supported.");
}

internal sealed class UpdatePreferencesCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<UpdatePreferencesCommand>(context)
{
    protected override Task<Result> ApplyAsync(UserProfile profile, UpdatePreferencesCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.UpdatePreferences(command.PreferredCulture, command.NotifyOnComment, command.NotifyOnReply));
}
