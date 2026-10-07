using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Authors.Common;

public sealed record AuthorSummaryDto(
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    int PublishedPostCount);

/// <summary>Ommaviy profil. Sozlamalar (til, bildirishnomalar) va media id'lari ko'rsatilmaydi.</summary>
public sealed record AuthorProfileDto(
    string Username,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? Bio,
    string? AboutMe,
    string? AvatarUrl,
    string? CoverUrl,
    DateOnly? BirthDate,
    string? Location,
    string? Profession,
    string? Company,
    string? Website,
    string? PublicEmail,
    string? Phone,
    DateTimeOffset MemberSince,
    int PublishedPostCount,
    IReadOnlyList<SocialLinkDto> SocialLinks,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<ExperienceDto> Experiences,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<CertificateDto> Certificates);

internal sealed record AuthorListItem(Guid Id, string Username, string DisplayName, Guid? AvatarMediaId, string? Bio);

/// <summary>Mualliflar ro'yxati (ommaviy): username yoki ism bo'yicha qidiruv.</summary>
internal sealed class AuthorsSpec : Specification<UserProfile, AuthorListItem>
{
    public AuthorsSpec(string? search, int? page = null, int? pageSize = null)
    {
        IgnoreOwnership();

        if (search?.Trim().ToLowerInvariant() is { Length: > 0 } term)
            Where(p => p.Username.ToLower().Contains(term) || p.DisplayName.ToLower().Contains(term));

        OrderByAsc(p => p.DisplayName);
        OrderByAsc(p => p.Username);
        Select(p => new AuthorListItem(p.Id, p.Username, p.DisplayName, p.AvatarMediaId, p.Bio));
        ReadOnly();

        if (page is { } pageNumber && pageSize is { } size)
            Paginate(pageNumber, size);
    }
}
