using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.Common;

/// <summary>Profil bilan birga yuklanadigan child kolleksiyalar (ular AutoInclude qilinmaydi).</summary>
[Flags]
internal enum ProfileSections
{
    None = 0,
    SocialLinks = 1,
    Skills = 2,
    Experiences = 4,
    Educations = 8,
    Certificates = 16,
    All = SocialLinks | Skills | Experiences | Educations | Certificates
}

/// <summary>Joriy foydalanuvchining profili (ownership filtri bilan) — kerakli bo'limlar bilan.</summary>
internal sealed class ProfileByIdSpec : Specification<UserProfile>
{
    public ProfileByIdSpec(Guid userId, ProfileSections sections, bool readOnly = false)
    {
        Where(p => p.Id == userId);
        IncludeSections(sections);
        if (readOnly)
            ReadOnly();
    }

    private void IncludeSections(ProfileSections sections)
    {
        if (sections.HasFlag(ProfileSections.SocialLinks))
            Include(p => p.SocialLinks);
        if (sections.HasFlag(ProfileSections.Skills))
            Include(p => p.Skills);
        if (sections.HasFlag(ProfileSections.Experiences))
            Include(p => p.Experiences);
        if (sections.HasFlag(ProfileSections.Educations))
            Include(p => p.Educations);
        if (sections.HasFlag(ProfileSections.Certificates))
            Include(p => p.Certificates);

        // Bir nechta kolleksiya — cartesian explosion bo'lmasligi uchun
        if (sections != ProfileSections.None)
            SplitQuery();
    }
}

/// <summary>Ommaviy profil: username bo'yicha (katta-kichik harfsiz), barcha bo'limlar bilan.</summary>
internal sealed class PublicProfileByUsernameSpec : Specification<UserProfile>
{
    public PublicProfileByUsernameSpec(string username)
    {
        var normalized = username.Trim().ToLowerInvariant();

        IgnoreOwnership();
        Where(p => p.Username.ToLower() == normalized);
        Include(p => p.SocialLinks);
        Include(p => p.Skills);
        Include(p => p.Experiences);
        Include(p => p.Educations);
        Include(p => p.Certificates);
        SplitQuery();
        ReadOnly();
    }
}

/// <summary>Username bo'yicha profil id'si (ommaviy so'rovlar uchun).</summary>
internal sealed class PublicProfileIdByUsernameSpec : Specification<UserProfile, PublicProfileRef>
{
    public PublicProfileIdByUsernameSpec(string username)
    {
        var normalized = username.Trim().ToLowerInvariant();

        IgnoreOwnership();
        Where(p => p.Username.ToLower() == normalized);
        Select(p => new PublicProfileRef(p.Id, p.Username));
        ReadOnly();
    }
}

internal sealed record PublicProfileRef(Guid Id, string Username);

/// <summary>Profil egasining username'i (keshni tozalash uchun).</summary>
internal sealed class ProfileUsernameSpec : Specification<UserProfile, string>
{
    public ProfileUsernameSpec(Guid userId)
    {
        IgnoreOwnership();
        Where(p => p.Id == userId);
        Select(p => p.Username);
        ReadOnly();
    }
}
