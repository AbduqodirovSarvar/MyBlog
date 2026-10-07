using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.Common;

public sealed record SocialLinkDto(Guid Id, SocialPlatform Platform, string Url, int Order);

public sealed record SkillDto(Guid Id, string Name, int? Level, int Order);

public sealed record ExperienceDto(
    Guid Id,
    string Company,
    string Position,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record EducationDto(
    Guid Id,
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description);

public sealed record CertificateDto(
    Guid Id,
    string Title,
    string? Issuer,
    DateOnly? IssuedAt,
    DateOnly? ExpiresAt,
    string? CredentialUrl,
    Guid? MediaId,
    string? MediaUrl);

/// <summary>Egasi uchun to'liq profil (barcha bo'limlar va sozlamalar bilan).</summary>
public sealed record ProfileDto(
    Guid Id,
    string Username,
    string? FirstName,
    string? LastName,
    string DisplayName,
    string? Bio,
    string? AboutMe,
    Guid? AvatarMediaId,
    string? AvatarUrl,
    Guid? CoverMediaId,
    string? CoverUrl,
    DateOnly? BirthDate,
    string? Location,
    string? Profession,
    string? Company,
    string? Website,
    string? PublicEmail,
    string? Phone,
    string PreferredCulture,
    bool NotifyOnComment,
    bool NotifyOnReply,
    IReadOnlyList<SocialLinkDto> SocialLinks,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<ExperienceDto> Experiences,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<CertificateDto> Certificates);
