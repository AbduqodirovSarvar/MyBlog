using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.UpdateAboutMe;

/// <param name="AboutMe">HTML; saqlashdan oldin sanitize qilinadi.</param>
public sealed record UpdateAboutMeCommand(string? AboutMe) : ICommand;

internal sealed class UpdateAboutMeCommandHandler(ProfileCommandContext context, IHtmlSanitizer sanitizer)
    : ProfileCommandHandler<UpdateAboutMeCommand>(context)
{
    protected override Task<Result> ApplyAsync(UserProfile profile, UpdateAboutMeCommand command,
        CancellationToken cancellationToken)
    {
        // Avval uzunlik: juda katta HTML'ni sanitize qilib o'tirmaymiz
        var html = DomainRules.TrimToNull(command.AboutMe);
        if (html?.Length > AboutMeMaxLength)
            return Task.FromResult<Result>(UserProfileErrors.AboutMeTooLong);

        var sanitized = html is null ? null : sanitizer.Sanitize(html);
        return Task.FromResult(profile.UpdateAboutMe(sanitized));
    }
}
