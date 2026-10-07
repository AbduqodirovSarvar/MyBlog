using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Profile.Certificates;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Profile.Educations;
using MyBlog.Application.Features.Profile.Experiences;
using MyBlog.Application.Features.Profile.GetMyProfile;
using MyBlog.Application.Features.Profile.SetProfileImage;
using MyBlog.Application.Features.Profile.Skills;
using MyBlog.Application.Features.Profile.SocialLinks;
using MyBlog.Application.Features.Profile.UpdateAboutMe;
using MyBlog.Application.Features.Profile.UpdateBasicInfo;
using MyBlog.Application.Features.Profile.UpdateContactInfo;
using MyBlog.Application.Features.Profile.UpdatePreferences;
using MyBlog.Domain.Users;

namespace MyBlog.Api.Controllers;

public sealed record MediaReferenceRequest(Guid? MediaId);

public sealed record ReorderRequest(IReadOnlyList<Guid> OrderedIds);

public sealed record SocialLinkRequest(SocialPlatform Platform, string Url);

public sealed record SkillRequest(string Name, int? Level);

public sealed record ExperienceRequest(
    string Company,
    string Position,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record EducationRequest(
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description);

public sealed record CertificateRequest(
    string Title,
    string? Issuer,
    DateOnly? IssuedAt,
    DateOnly? ExpiresAt,
    string? CredentialUrl,
    Guid? MediaId);

/// <summary>Joriy foydalanuvchining profilini boshqarish.</summary>
[Route("api/my/profile")]
[HasPermission(Permissions.Profile.Manage)]
public sealed class MyProfileController(ISender sender) : ApiController(sender)
{
    private const string ProfileLocation = "/api/my/profile";

    [HttpGet]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await Sender.Send(new GetMyProfileQuery(), cancellationToken)).ToActionResult();

    [HttpPut("basic")]
    public async Task<IActionResult> UpdateBasic(UpdateBasicInfoCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPut("contact")]
    public async Task<IActionResult> UpdateContact(UpdateContactInfoCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPut("about")]
    public async Task<IActionResult> UpdateAbout(UpdateAboutMeCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(UpdatePreferencesCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPut("avatar")]
    public async Task<IActionResult> SetAvatar(MediaReferenceRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new SetProfileImageCommand(ProfileImageKind.Avatar, request.MediaId), cancellationToken))
        .ToActionResult();

    [HttpPut("cover")]
    public async Task<IActionResult> SetCover(MediaReferenceRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new SetProfileImageCommand(ProfileImageKind.Cover, request.MediaId), cancellationToken))
        .ToActionResult();

    // ---------- Ijtimoiy tarmoqlar ----------

    [HttpPost("social-links")]
    [ProducesResponseType<SocialLinkDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddSocialLink(SocialLinkRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new AddSocialLinkCommand(request.Platform, request.Url), cancellationToken))
        .ToCreatedResult(_ => ProfileLocation);

    [HttpPut("social-links/{id:guid}")]
    public async Task<IActionResult> UpdateSocialLink(Guid id, SocialLinkRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateSocialLinkCommand(id, request.Platform, request.Url), cancellationToken))
        .ToActionResult();

    [HttpDelete("social-links/{id:guid}")]
    public async Task<IActionResult> RemoveSocialLink(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveSocialLinkCommand(id), cancellationToken)).ToActionResult();

    [HttpPut("social-links/order")]
    public async Task<IActionResult> ReorderSocialLinks(ReorderRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new ReorderSocialLinksCommand(request.OrderedIds), cancellationToken)).ToActionResult();

    // ---------- Ko'nikmalar ----------

    [HttpPost("skills")]
    [ProducesResponseType<SkillDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddSkill(SkillRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new AddSkillCommand(request.Name, request.Level), cancellationToken))
        .ToCreatedResult(_ => ProfileLocation);

    [HttpPut("skills/{id:guid}")]
    public async Task<IActionResult> UpdateSkill(Guid id, SkillRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateSkillCommand(id, request.Name, request.Level), cancellationToken)).ToActionResult();

    [HttpDelete("skills/{id:guid}")]
    public async Task<IActionResult> RemoveSkill(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveSkillCommand(id), cancellationToken)).ToActionResult();

    [HttpPut("skills/order")]
    public async Task<IActionResult> ReorderSkills(ReorderRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new ReorderSkillsCommand(request.OrderedIds), cancellationToken)).ToActionResult();

    // ---------- Ish tajribasi ----------

    [HttpPost("experiences")]
    [ProducesResponseType<ExperienceDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddExperience(ExperienceRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new AddExperienceCommand(r.Company, r.Position, r.Location, r.StartDate, r.EndDate, r.IsCurrent,
            r.Description), cancellationToken))
        .ToCreatedResult(_ => ProfileLocation);

    [HttpPut("experiences/{id:guid}")]
    public async Task<IActionResult> UpdateExperience(Guid id, ExperienceRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateExperienceCommand(id, r.Company, r.Position, r.Location, r.StartDate, r.EndDate,
            r.IsCurrent, r.Description), cancellationToken))
        .ToActionResult();

    [HttpDelete("experiences/{id:guid}")]
    public async Task<IActionResult> RemoveExperience(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveExperienceCommand(id), cancellationToken)).ToActionResult();

    // ---------- Ta'lim ----------

    [HttpPost("educations")]
    [ProducesResponseType<EducationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEducation(EducationRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new AddEducationCommand(r.Institution, r.Degree, r.FieldOfStudy, r.StartDate, r.EndDate,
            r.Description), cancellationToken))
        .ToCreatedResult(_ => ProfileLocation);

    [HttpPut("educations/{id:guid}")]
    public async Task<IActionResult> UpdateEducation(Guid id, EducationRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateEducationCommand(id, r.Institution, r.Degree, r.FieldOfStudy, r.StartDate, r.EndDate,
            r.Description), cancellationToken))
        .ToActionResult();

    [HttpDelete("educations/{id:guid}")]
    public async Task<IActionResult> RemoveEducation(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveEducationCommand(id), cancellationToken)).ToActionResult();

    // ---------- Sertifikatlar ----------

    [HttpPost("certificates")]
    [ProducesResponseType<CertificateDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddCertificate(CertificateRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new AddCertificateCommand(r.Title, r.Issuer, r.IssuedAt, r.ExpiresAt, r.CredentialUrl,
            r.MediaId), cancellationToken))
        .ToCreatedResult(_ => ProfileLocation);

    [HttpPut("certificates/{id:guid}")]
    public async Task<IActionResult> UpdateCertificate(Guid id, CertificateRequest r, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateCertificateCommand(id, r.Title, r.Issuer, r.IssuedAt, r.ExpiresAt, r.CredentialUrl,
            r.MediaId), cancellationToken))
        .ToActionResult();

    [HttpDelete("certificates/{id:guid}")]
    public async Task<IActionResult> RemoveCertificate(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveCertificateCommand(id), cancellationToken)).ToActionResult();
}
