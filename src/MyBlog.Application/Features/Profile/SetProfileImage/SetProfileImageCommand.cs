using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.SetProfileImage;

public enum ProfileImageKind
{
    Avatar = 1,
    Cover = 2
}

/// <param name="MediaId">null — rasmni olib tashlash.</param>
public sealed record SetProfileImageCommand(ProfileImageKind Kind, Guid? MediaId) : ICommand;

internal sealed class SetProfileImageCommandHandler(ProfileCommandContext context, IReadRepository<MediaFile> mediaFiles)
    : ProfileCommandHandler<SetProfileImageCommand>(context)
{
    protected override async Task<Result> ApplyAsync(UserProfile profile, SetProfileImageCommand command,
        CancellationToken cancellationToken)
    {
        var mediaId = command.MediaId == Guid.Empty ? null : command.MediaId;

        if (mediaId is { } id)
        {
            // Ownership filtri tufayli faqat o'z fayllari topiladi
            var media = await mediaFiles.GetByIdAsync(id, cancellationToken);
            if (media is null)
                return MediaErrors.NotFound;
            if (!media.IsImage)
                return MediaErrors.UnsupportedFileType;
        }

        if (command.Kind == ProfileImageKind.Avatar)
            profile.SetAvatar(mediaId);
        else
            profile.SetCover(mediaId);

        return Result.Success();
    }
}
