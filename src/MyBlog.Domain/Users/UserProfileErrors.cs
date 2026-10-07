using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

public static class UserProfileErrors
{
    public static readonly Error NotFound = Error.NotFound("UserProfile.NotFound", "User profile was not found.");
    public static readonly Error InvalidUserId = Error.Validation("UserProfile.InvalidUserId", "User id is required.");
    public static readonly Error UsernameTaken = Error.Conflict("UserProfile.UsernameTaken", "Username is already taken.");

    public static readonly Error UsernameInvalid = Error.Validation("UserProfile.UsernameInvalid",
            "Username must be {0}-{1} characters long and contain only letters, digits, '.', '_' or '-'.")
        .WithArgs(UsernameMinLength, UsernameMaxLength);

    public static readonly Error FirstNameTooLong = TooLong("UserProfile.FirstNameTooLong", "First name", FirstNameMaxLength);
    public static readonly Error LastNameTooLong = TooLong("UserProfile.LastNameTooLong", "Last name", LastNameMaxLength);
    public static readonly Error DisplayNameTooLong = TooLong("UserProfile.DisplayNameTooLong", "Display name", DisplayNameMaxLength);
    public static readonly Error BioTooLong = TooLong("UserProfile.BioTooLong", "Bio", BioMaxLength);
    public static readonly Error AboutMeTooLong = TooLong("UserProfile.AboutMeTooLong", "About me", AboutMeMaxLength);
    public static readonly Error LocationTooLong = TooLong("UserProfile.LocationTooLong", "Location", LocationMaxLength);
    public static readonly Error ProfessionTooLong = TooLong("UserProfile.ProfessionTooLong", "Profession", ProfessionMaxLength);
    public static readonly Error CompanyTooLong = TooLong("UserProfile.CompanyTooLong", "Company", CompanyMaxLength);
    public static readonly Error PhoneTooLong = TooLong("UserProfile.PhoneTooLong", "Phone", PhoneMaxLength);

    public static readonly Error WebsiteInvalid = Error.Validation("UserProfile.WebsiteInvalid", "Website must be a valid http(s) URL.");
    public static readonly Error PublicEmailInvalid = Error.Validation("UserProfile.PublicEmailInvalid", "Public email is not a valid email address.");
    public static readonly Error CultureNotSupported = Error.Validation("UserProfile.CultureNotSupported", "Culture '{0}' is not supported.");

    // Ijtimoiy tarmoqlar
    public static readonly Error SocialLinkNotFound = Error.NotFound("UserProfile.SocialLinkNotFound", "Social link was not found.");
    public static readonly Error SocialLinkUrlInvalid = Error.Validation("UserProfile.SocialLinkUrlInvalid", "Social link URL must be a valid http(s) URL.");
    public static readonly Error SocialPlatformInvalid = Error.Validation("UserProfile.SocialPlatformInvalid", "Social platform is not valid.");
    public static readonly Error TooManySocialLinks = TooMany("UserProfile.TooManySocialLinks", "social links", MaxSocialLinks);

    // Ko'nikmalar
    public static readonly Error SkillNotFound = Error.NotFound("UserProfile.SkillNotFound", "Skill was not found.");
    public static readonly Error SkillNameRequired = Error.Validation("UserProfile.SkillNameRequired", "Skill name is required.");
    public static readonly Error SkillNameTooLong = TooLong("UserProfile.SkillNameTooLong", "Skill name", SkillNameMaxLength);
    public static readonly Error SkillDuplicate = Error.Conflict("UserProfile.SkillDuplicate", "Skill with the same name already exists.");
    public static readonly Error TooManySkills = TooMany("UserProfile.TooManySkills", "skills", MaxSkills);

    public static readonly Error SkillLevelOutOfRange = Error.Validation("UserProfile.SkillLevelOutOfRange",
            "Skill level must be between {0} and {1}.")
        .WithArgs(SkillLevelMin, SkillLevelMax);

    // Ish tajribasi
    public static readonly Error ExperienceNotFound = Error.NotFound("UserProfile.ExperienceNotFound", "Experience was not found.");
    public static readonly Error ExperienceCompanyRequired = Error.Validation("UserProfile.ExperienceCompanyRequired", "Company is required.");
    public static readonly Error ExperiencePositionRequired = Error.Validation("UserProfile.ExperiencePositionRequired", "Position is required.");
    public static readonly Error PositionTooLong = TooLong("UserProfile.PositionTooLong", "Position", PositionMaxLength);
    public static readonly Error TooManyExperiences = TooMany("UserProfile.TooManyExperiences", "experiences", MaxExperiences);

    // Ta'lim
    public static readonly Error EducationNotFound = Error.NotFound("UserProfile.EducationNotFound", "Education was not found.");
    public static readonly Error InstitutionRequired = Error.Validation("UserProfile.InstitutionRequired", "Institution is required.");
    public static readonly Error InstitutionTooLong = TooLong("UserProfile.InstitutionTooLong", "Institution", InstitutionMaxLength);
    public static readonly Error DegreeTooLong = TooLong("UserProfile.DegreeTooLong", "Degree", DegreeMaxLength);
    public static readonly Error FieldOfStudyTooLong = TooLong("UserProfile.FieldOfStudyTooLong", "Field of study", FieldOfStudyMaxLength);
    public static readonly Error TooManyEducations = TooMany("UserProfile.TooManyEducations", "educations", MaxEducations);

    // Sertifikatlar
    public static readonly Error CertificateNotFound = Error.NotFound("UserProfile.CertificateNotFound", "Certificate was not found.");
    public static readonly Error CertificateTitleRequired = Error.Validation("UserProfile.CertificateTitleRequired", "Certificate title is required.");
    public static readonly Error CertificateTitleTooLong = TooLong("UserProfile.CertificateTitleTooLong", "Certificate title", CertificateTitleMaxLength);
    public static readonly Error IssuerTooLong = TooLong("UserProfile.IssuerTooLong", "Issuer", IssuerMaxLength);
    public static readonly Error CredentialUrlInvalid = Error.Validation("UserProfile.CredentialUrlInvalid", "Credential URL must be a valid http(s) URL.");
    public static readonly Error CertificateExpiresBeforeIssued = Error.Validation("UserProfile.CertificateExpiresBeforeIssued", "Expiration date cannot be earlier than the issue date.");
    public static readonly Error TooManyCertificates = TooMany("UserProfile.TooManyCertificates", "certificates", MaxCertificates);

    // Umumiy
    public static readonly Error DescriptionTooLong = TooLong("UserProfile.DescriptionTooLong", "Description", DescriptionMaxLength);
    public static readonly Error EndDateBeforeStartDate = Error.Validation("UserProfile.EndDateBeforeStartDate", "End date cannot be earlier than the start date.");
    public static readonly Error CurrentWithEndDate = Error.Validation("UserProfile.CurrentWithEndDate", "A current position cannot have an end date.");
    public static readonly Error ReorderMismatch = Error.Validation("UserProfile.ReorderMismatch", "The ordered list must contain every existing item exactly once.");

    public static Error CultureNotSupportedFor(string culture) => CultureNotSupported.WithArgs(culture);

    private static Error TooLong(string code, string field, int max) =>
        Error.Validation(code, $"{field} must not exceed {{0}} characters.").WithArgs(max);

    private static Error TooMany(string code, string items, int max) =>
        Error.Validation(code, $"You cannot add more than {{0}} {items}.").WithArgs(max);
}
