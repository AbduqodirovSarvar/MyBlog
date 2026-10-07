using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Auth.Common;

internal sealed record ProfileSummary(string DisplayName, Guid? AvatarMediaId, string PreferredCulture);

// Auth so'rovlari ko'pincha anonim (login, refresh) — ownership filtri o'chiriladi.

internal sealed class ProfileSummarySpec : Specification<UserProfile, ProfileSummary>
{
    public ProfileSummarySpec(Guid userId)
    {
        Where(p => p.Id == userId);
        IgnoreOwnership();
        ReadOnly();
        Select(p => new ProfileSummary(p.DisplayName, p.AvatarMediaId, p.PreferredCulture));
    }
}

internal sealed class ProfileUsernameExistsSpec : Specification<UserProfile>
{
    public ProfileUsernameExistsSpec(string username)
    {
        var normalized = username.Trim().ToLowerInvariant();
        Where(p => p.Username.ToLower() == normalized);
        IgnoreOwnership();
    }
}

internal sealed class MediaStorageKeySpec : Specification<MediaFile, string>
{
    public MediaStorageKeySpec(Guid mediaId)
    {
        Where(m => m.Id == mediaId);
        IgnoreOwnership();
        ReadOnly();
        Select(m => m.StorageKey);
    }
}
