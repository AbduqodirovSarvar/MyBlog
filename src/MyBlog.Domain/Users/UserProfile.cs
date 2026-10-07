using System.Text.RegularExpressions;
using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

/// <summary>
/// Foydalanuvchining ommaviy profili. Id identity user id bilan bir xil, OwnerId == Id.
/// Child entity'lar (social link, skill, ...) faqat shu aggregate metodlari orqali o'zgartiriladi.
/// </summary>
public sealed partial class UserProfile : AuditableEntity, IAggregateRoot, IOwnedEntity
{
    private readonly List<SocialLink> _socialLinks = [];
    private readonly List<Skill> _skills = [];
    private readonly List<Experience> _experiences = [];
    private readonly List<Education> _educations = [];
    private readonly List<Certificate> _certificates = [];

    private UserProfile() { }

    private UserProfile(Guid userId, string username) : base(userId)
    {
        OwnerId = userId;
        Username = username;
        DisplayName = username;
    }

    public Guid OwnerId { get; private set; }
    public string Username { get; private set; } = null!;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string DisplayName { get; private set; } = null!;

    /// <summary>Qisqa plain text tavsif.</summary>
    public string? Bio { get; private set; }

    /// <summary>Sanitize qilingan HTML.</summary>
    public string? AboutMe { get; private set; }

    public Guid? AvatarMediaId { get; private set; }
    public Guid? CoverMediaId { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? Location { get; private set; }
    public string? Profession { get; private set; }
    public string? Company { get; private set; }
    public string? Website { get; private set; }
    public string? PublicEmail { get; private set; }
    public string? Phone { get; private set; }
    public string PreferredCulture { get; private set; } = Cultures.Default;
    public bool NotifyOnComment { get; private set; } = true;
    public bool NotifyOnReply { get; private set; } = true;

    public IReadOnlyCollection<SocialLink> SocialLinks => _socialLinks.AsReadOnly();
    public IReadOnlyCollection<Skill> Skills => _skills.AsReadOnly();
    public IReadOnlyCollection<Experience> Experiences => _experiences.AsReadOnly();
    public IReadOnlyCollection<Education> Educations => _educations.AsReadOnly();
    public IReadOnlyCollection<Certificate> Certificates => _certificates.AsReadOnly();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    public static bool IsValidUsername(string? username) =>
        username is { Length: >= UsernameMinLength and <= UsernameMaxLength } && UsernameRegex().IsMatch(username);

    public static Result<UserProfile> Create(Guid userId, string username, string? firstName = null,
        string? lastName = null, string? preferredCulture = null)
    {
        if (userId == Guid.Empty)
            return UserProfileErrors.InvalidUserId;

        var usernameValue = username?.Trim();
        if (!IsValidUsername(usernameValue))
            return UserProfileErrors.UsernameInvalid;

        var profile = new UserProfile(userId, usernameValue!);

        var names = profile.SetNames(firstName, lastName, displayName: null);
        if (names.IsFailure)
            return names.Error;

        if (preferredCulture is not null)
        {
            var culture = Cultures.Normalize(preferredCulture);
            if (culture is null)
                return UserProfileErrors.CultureNotSupportedFor(preferredCulture);
            profile.PreferredCulture = culture;
        }

        return profile;
    }

    public Result ChangeUsername(string username)
    {
        var value = username?.Trim();
        if (!IsValidUsername(value))
            return UserProfileErrors.UsernameInvalid;

        Username = value!;
        return Result.Success();
    }

    /// <summary>Asosiy ma'lumotlar. displayName bo'sh bo'lsa ism-familiyadan (yoki username'dan) olinadi.</summary>
    public Result UpdateBasicInfo(string? firstName, string? lastName, string? displayName, string? bio,
        DateOnly? birthDate, string? location, string? profession, string? company)
    {
        var bioValue = DomainRules.TrimToNull(bio);
        var locationValue = DomainRules.TrimToNull(location);
        var professionValue = DomainRules.TrimToNull(profession);
        var companyValue = DomainRules.TrimToNull(company);

        if (bioValue?.Length > BioMaxLength)
            return UserProfileErrors.BioTooLong;
        if (locationValue?.Length > LocationMaxLength)
            return UserProfileErrors.LocationTooLong;
        if (professionValue?.Length > ProfessionMaxLength)
            return UserProfileErrors.ProfessionTooLong;
        if (companyValue?.Length > CompanyMaxLength)
            return UserProfileErrors.CompanyTooLong;

        var names = SetNames(firstName, lastName, displayName);
        if (names.IsFailure)
            return names;

        Bio = bioValue;
        BirthDate = birthDate;
        Location = locationValue;
        Profession = professionValue;
        Company = companyValue;
        return Result.Success();
    }

    public Result UpdateContactInfo(string? website, string? publicEmail, string? phone)
    {
        var websiteValue = DomainRules.TrimToNull(website);
        var emailValue = DomainRules.TrimToNull(publicEmail);
        var phoneValue = DomainRules.TrimToNull(phone);

        if (websiteValue is not null && (websiteValue.Length > UrlMaxLength || !DomainRules.IsValidHttpUrl(websiteValue)))
            return UserProfileErrors.WebsiteInvalid;
        if (emailValue is not null && (emailValue.Length > EmailMaxLength || !DomainRules.IsValidEmail(emailValue)))
            return UserProfileErrors.PublicEmailInvalid;
        if (phoneValue?.Length > PhoneMaxLength)
            return UserProfileErrors.PhoneTooLong;

        Website = websiteValue;
        PublicEmail = emailValue;
        Phone = phoneValue;
        return Result.Success();
    }

    /// <param name="aboutMeHtml">Application qatlamida sanitize qilingan HTML.</param>
    public Result UpdateAboutMe(string? aboutMeHtml)
    {
        var value = DomainRules.TrimToNull(aboutMeHtml);
        if (value?.Length > AboutMeMaxLength)
            return UserProfileErrors.AboutMeTooLong;

        AboutMe = value;
        return Result.Success();
    }

    public void SetAvatar(Guid? mediaId) => AvatarMediaId = mediaId == Guid.Empty ? null : mediaId;

    public void SetCover(Guid? mediaId) => CoverMediaId = mediaId == Guid.Empty ? null : mediaId;

    public Result UpdatePreferences(string preferredCulture, bool notifyOnComment, bool notifyOnReply)
    {
        var culture = Cultures.Normalize(preferredCulture);
        if (culture is null)
            return UserProfileErrors.CultureNotSupportedFor(preferredCulture ?? string.Empty);

        PreferredCulture = culture;
        NotifyOnComment = notifyOnComment;
        NotifyOnReply = notifyOnReply;
        return Result.Success();
    }

    // ---------- Social links ----------

    public Result<SocialLink> AddSocialLink(SocialPlatform platform, string url)
    {
        if (_socialLinks.Count >= MaxSocialLinks)
            return UserProfileErrors.TooManySocialLinks;

        var link = SocialLink.Create(Id, platform, url, NextOrder(_socialLinks));
        if (link.IsSuccess)
            _socialLinks.Add(link.Value);
        return link;
    }

    public Result UpdateSocialLink(Guid linkId, SocialPlatform platform, string url) =>
        _socialLinks.Find(l => l.Id == linkId) is { } link
            ? link.Update(platform, url)
            : UserProfileErrors.SocialLinkNotFound;

    public Result RemoveSocialLink(Guid linkId) => RemoveOrdered(_socialLinks, linkId, UserProfileErrors.SocialLinkNotFound);

    public Result ReorderSocialLinks(IReadOnlyList<Guid> orderedIds) => Reorder(_socialLinks, orderedIds);

    // ---------- Skills ----------

    public Result<Skill> AddSkill(string name, int? level)
    {
        if (_skills.Count >= MaxSkills)
            return UserProfileErrors.TooManySkills;
        if (HasSkillNamed(name, exceptId: null))
            return UserProfileErrors.SkillDuplicate;

        var skill = Skill.Create(Id, name, level, NextOrder(_skills));
        if (skill.IsSuccess)
            _skills.Add(skill.Value);
        return skill;
    }

    public Result UpdateSkill(Guid skillId, string name, int? level)
    {
        var skill = _skills.Find(s => s.Id == skillId);
        if (skill is null)
            return UserProfileErrors.SkillNotFound;
        if (HasSkillNamed(name, exceptId: skillId))
            return UserProfileErrors.SkillDuplicate;

        return skill.Update(name, level);
    }

    public Result RemoveSkill(Guid skillId) => RemoveOrdered(_skills, skillId, UserProfileErrors.SkillNotFound);

    public Result ReorderSkills(IReadOnlyList<Guid> orderedIds) => Reorder(_skills, orderedIds);

    // ---------- Experiences ----------

    public Result<Experience> AddExperience(string company, string position, string? location,
        DateOnly startDate, DateOnly? endDate, bool isCurrent, string? description)
    {
        if (_experiences.Count >= MaxExperiences)
            return UserProfileErrors.TooManyExperiences;

        var experience = Experience.Create(Id, company, position, location, startDate, endDate, isCurrent, description);
        if (experience.IsSuccess)
            _experiences.Add(experience.Value);
        return experience;
    }

    public Result UpdateExperience(Guid experienceId, string company, string position, string? location,
        DateOnly startDate, DateOnly? endDate, bool isCurrent, string? description) =>
        _experiences.Find(e => e.Id == experienceId) is { } experience
            ? experience.Update(company, position, location, startDate, endDate, isCurrent, description)
            : UserProfileErrors.ExperienceNotFound;

    public Result RemoveExperience(Guid experienceId) =>
        _experiences.RemoveAll(e => e.Id == experienceId) > 0
            ? Result.Success()
            : UserProfileErrors.ExperienceNotFound;

    // ---------- Educations ----------

    public Result<Education> AddEducation(string institution, string? degree, string? fieldOfStudy,
        DateOnly startDate, DateOnly? endDate, string? description)
    {
        if (_educations.Count >= MaxEducations)
            return UserProfileErrors.TooManyEducations;

        var education = Education.Create(Id, institution, degree, fieldOfStudy, startDate, endDate, description);
        if (education.IsSuccess)
            _educations.Add(education.Value);
        return education;
    }

    public Result UpdateEducation(Guid educationId, string institution, string? degree, string? fieldOfStudy,
        DateOnly startDate, DateOnly? endDate, string? description) =>
        _educations.Find(e => e.Id == educationId) is { } education
            ? education.Update(institution, degree, fieldOfStudy, startDate, endDate, description)
            : UserProfileErrors.EducationNotFound;

    public Result RemoveEducation(Guid educationId) =>
        _educations.RemoveAll(e => e.Id == educationId) > 0
            ? Result.Success()
            : UserProfileErrors.EducationNotFound;

    // ---------- Certificates ----------

    public Result<Certificate> AddCertificate(string title, string? issuer, DateOnly? issuedAt,
        DateOnly? expiresAt, string? credentialUrl, Guid? mediaId)
    {
        if (_certificates.Count >= MaxCertificates)
            return UserProfileErrors.TooManyCertificates;

        var certificate = Certificate.Create(Id, title, issuer, issuedAt, expiresAt, credentialUrl, mediaId);
        if (certificate.IsSuccess)
            _certificates.Add(certificate.Value);
        return certificate;
    }

    public Result UpdateCertificate(Guid certificateId, string title, string? issuer, DateOnly? issuedAt,
        DateOnly? expiresAt, string? credentialUrl, Guid? mediaId) =>
        _certificates.Find(c => c.Id == certificateId) is { } certificate
            ? certificate.Update(title, issuer, issuedAt, expiresAt, credentialUrl, mediaId)
            : UserProfileErrors.CertificateNotFound;

    public Result RemoveCertificate(Guid certificateId) =>
        _certificates.RemoveAll(c => c.Id == certificateId) > 0
            ? Result.Success()
            : UserProfileErrors.CertificateNotFound;

    // ---------- Yordamchi metodlar ----------

    private Result SetNames(string? firstName, string? lastName, string? displayName)
    {
        var first = DomainRules.TrimToNull(firstName);
        var last = DomainRules.TrimToNull(lastName);
        var display = DomainRules.TrimToNull(displayName);

        if (first?.Length > FirstNameMaxLength)
            return UserProfileErrors.FirstNameTooLong;
        if (last?.Length > LastNameMaxLength)
            return UserProfileErrors.LastNameTooLong;

        display ??= DomainRules.TrimToNull($"{first} {last}") ?? Username;
        if (display.Length > DisplayNameMaxLength)
            return UserProfileErrors.DisplayNameTooLong;

        FirstName = first;
        LastName = last;
        DisplayName = display;
        return Result.Success();
    }

    private bool HasSkillNamed(string? name, Guid? exceptId)
    {
        var value = name?.Trim();
        return value is not null && _skills.Exists(s =>
            s.Id != exceptId && string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase));
    }

    private static int NextOrder<T>(List<T> items) where T : IOrderedItem =>
        items.Count == 0 ? 0 : items.Max(i => i.Order) + 1;

    private static Result RemoveOrdered<T>(List<T> items, Guid id, Error notFound) where T : IOrderedItem
    {
        if (items.RemoveAll(i => i.Id == id) == 0)
            return notFound;

        // Qolganlarini 0..n-1 ga qayta raqamlaymiz
        var index = 0;
        foreach (var item in items.OrderBy(i => i.Order))
            item.SetOrder(index++);

        return Result.Success();
    }

    private static Result Reorder<T>(List<T> items, IReadOnlyList<Guid> orderedIds) where T : IOrderedItem
    {
        if (orderedIds.Count != items.Count || orderedIds.Distinct().Count() != orderedIds.Count)
            return UserProfileErrors.ReorderMismatch;

        var byId = items.ToDictionary(i => i.Id);
        if (!orderedIds.All(byId.ContainsKey))
            return UserProfileErrors.ReorderMismatch;

        for (var i = 0; i < orderedIds.Count; i++)
            byId[orderedIds[i]].SetOrder(i);

        return Result.Success();
    }
}
