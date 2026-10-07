using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Authors.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Authors.GetAuthorProfile;

public sealed record GetAuthorProfileQuery(string Username) : IQuery<AuthorProfileDto>;

internal sealed class GetAuthorProfileQueryHandler(
    IReadRepository<UserProfile> profiles,
    IContentStatsRepository stats,
    IMediaUrlResolver mediaUrls,
    ICacheService cache) : IQueryHandler<GetAuthorProfileQuery, AuthorProfileDto>
{
    public async Task<Result<AuthorProfileDto>> Handle(GetAuthorProfileQuery request, CancellationToken cancellationToken)
    {
        if (!UserProfile.IsValidUsername(request.Username?.Trim()))
            return UserProfileErrors.NotFound;

        var key = AuthorCacheKeys.Profile(request.Username!);
        var dto = await cache.GetOrCreateAsync(key, ct => LoadAsync(request.Username!, ct), AuthorCacheKeys.Expiration,
            [AuthorCacheKeys.Tag(request.Username!)], cancellationToken);

        if (dto is null)
        {
            // "Topilmadi" keshlanmaydi: foydalanuvchi keyinroq ro'yxatdan o'tishi mumkin
            await cache.RemoveAsync(key, cancellationToken);
            return UserProfileErrors.NotFound;
        }

        return dto;
    }

    private async Task<AuthorProfileDto?> LoadAsync(string username, CancellationToken cancellationToken)
    {
        var profile = await profiles.FirstOrDefaultAsync(new PublicProfileByUsernameSpec(username), cancellationToken);
        if (profile is null)
            return null;

        var counts = await stats.CountPublishedPostsByOwnerAsync([profile.Id], cancellationToken);
        var urls = await ProfileMapper.ResolveUrlsAsync(mediaUrls, profile, publicAccess: true, cancellationToken);

        return new AuthorProfileDto(
            profile.Username,
            profile.DisplayName,
            profile.FirstName,
            profile.LastName,
            profile.Bio,
            profile.AboutMe,
            urls.UrlFor(profile.AvatarMediaId),
            urls.UrlFor(profile.CoverMediaId),
            profile.BirthDate,
            profile.Location,
            profile.Profession,
            profile.Company,
            profile.Website,
            profile.PublicEmail,
            profile.Phone,
            profile.CreatedAt,
            counts.GetValueOrDefault(profile.Id),
            ProfileMapper.SocialLinks(profile),
            ProfileMapper.Skills(profile),
            ProfileMapper.Experiences(profile),
            ProfileMapper.Educations(profile),
            ProfileMapper.Certificates(profile, urls));
    }
}
