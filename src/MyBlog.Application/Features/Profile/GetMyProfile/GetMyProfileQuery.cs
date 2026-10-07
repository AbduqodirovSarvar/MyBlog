using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.GetMyProfile;

public sealed record GetMyProfileQuery : IQuery<ProfileDto>;

internal sealed class GetMyProfileQueryHandler(
    IReadRepository<UserProfile> profiles,
    ICurrentUser currentUser,
    IMediaUrlResolver mediaUrls) : IQueryHandler<GetMyProfileQuery, ProfileDto>
{
    public async Task<Result<ProfileDto>> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var profile = await profiles.FirstOrDefaultAsync(
            new ProfileByIdSpec(currentUser.RequiredId, ProfileSections.All, readOnly: true), cancellationToken);
        if (profile is null)
            return UserProfileErrors.NotFound;

        var urls = await ProfileMapper.ResolveUrlsAsync(mediaUrls, profile, publicAccess: false, cancellationToken);
        return ProfileMapper.ToDto(profile, urls);
    }
}
