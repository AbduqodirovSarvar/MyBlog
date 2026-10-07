using MyBlog.Domain.Media;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.Common;

internal static class ProfileMapper
{
    public const string AvatarVariant = MediaVariant.Medium;
    public const string CoverVariant = MediaVariant.Large;

    /// <summary>Profil uchun kerakli barcha media id'lari (avatar va cover alohida variantda olinadi).</summary>
    public static IEnumerable<Guid?> CertificateMediaIds(UserProfile profile) =>
        profile.Certificates.Select(c => c.MediaId);

    public static ProfileDto ToDto(UserProfile profile, IReadOnlyDictionary<Guid, string> urls) => new(
        profile.Id,
        profile.Username,
        profile.FirstName,
        profile.LastName,
        profile.DisplayName,
        profile.Bio,
        profile.AboutMe,
        profile.AvatarMediaId,
        urls.UrlFor(profile.AvatarMediaId),
        profile.CoverMediaId,
        urls.UrlFor(profile.CoverMediaId),
        profile.BirthDate,
        profile.Location,
        profile.Profession,
        profile.Company,
        profile.Website,
        profile.PublicEmail,
        profile.Phone,
        profile.PreferredCulture,
        profile.NotifyOnComment,
        profile.NotifyOnReply,
        SocialLinks(profile),
        Skills(profile),
        Experiences(profile),
        Educations(profile),
        Certificates(profile, urls));

    public static IReadOnlyList<SocialLinkDto> SocialLinks(UserProfile profile) =>
        profile.SocialLinks.OrderBy(l => l.Order).Select(ToDto).ToList();

    public static IReadOnlyList<SkillDto> Skills(UserProfile profile) =>
        profile.Skills.OrderBy(s => s.Order).Select(ToDto).ToList();

    // Tajriba va ta'limda tartib maydoni yo'q — eng yangisi birinchi
    public static IReadOnlyList<ExperienceDto> Experiences(UserProfile profile) =>
        profile.Experiences
            .OrderByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate)
            .Select(ToDto)
            .ToList();

    public static IReadOnlyList<EducationDto> Educations(UserProfile profile) =>
        profile.Educations.OrderByDescending(e => e.StartDate).Select(ToDto).ToList();

    public static IReadOnlyList<CertificateDto> Certificates(UserProfile profile, IReadOnlyDictionary<Guid, string> urls) =>
        profile.Certificates
            .OrderByDescending(c => c.IssuedAt)
            .Select(c => ToDto(c, urls))
            .ToList();

    public static SocialLinkDto ToDto(SocialLink link) => new(link.Id, link.Platform, link.Url, link.Order);

    public static SkillDto ToDto(Skill skill) => new(skill.Id, skill.Name, skill.Level, skill.Order);

    public static ExperienceDto ToDto(Experience e) =>
        new(e.Id, e.Company, e.Position, e.Location, e.StartDate, e.EndDate, e.IsCurrent, e.Description);

    public static EducationDto ToDto(Education e) =>
        new(e.Id, e.Institution, e.Degree, e.FieldOfStudy, e.StartDate, e.EndDate, e.Description);

    public static CertificateDto ToDto(Certificate c, IReadOnlyDictionary<Guid, string> urls) =>
        new(c.Id, c.Title, c.Issuer, c.IssuedAt, c.ExpiresAt, c.CredentialUrl, c.MediaId, urls.UrlFor(c.MediaId));

    /// <summary>Avatar (medium), cover (large) va sertifikat fayllari URL'lari bitta lug'atda.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveUrlsAsync(IMediaUrlResolver resolver,
        UserProfile profile, bool publicAccess, CancellationToken cancellationToken)
    {
        var avatar = await resolver.ResolveAsync([profile.AvatarMediaId], publicAccess, AvatarVariant, cancellationToken);
        var cover = await resolver.ResolveAsync([profile.CoverMediaId], publicAccess, CoverVariant, cancellationToken);
        var certificates = await resolver.ResolveAsync(CertificateMediaIds(profile), publicAccess, null, cancellationToken);

        var merged = new Dictionary<Guid, string>(certificates);
        foreach (var (id, url) in avatar.Concat(cover))
            merged[id] = url;
        return merged;
    }
}
