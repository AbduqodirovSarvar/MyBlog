namespace MyBlog.Domain.Users;

public static class UserProfileConstraints
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 32;
    public const int FirstNameMaxLength = 100;
    public const int LastNameMaxLength = 100;
    public const int DisplayNameMaxLength = 150;
    public const int BioMaxLength = 500;
    public const int AboutMeMaxLength = 20_000;
    public const int LocationMaxLength = 150;
    public const int ProfessionMaxLength = 150;
    public const int CompanyMaxLength = 150;
    public const int UrlMaxLength = 500;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 32;

    // Ichki (child) entity'lar
    public const int SkillNameMaxLength = 100;
    public const int SkillLevelMin = 1;
    public const int SkillLevelMax = 100;
    public const int PositionMaxLength = 150;
    public const int InstitutionMaxLength = 200;
    public const int DegreeMaxLength = 150;
    public const int FieldOfStudyMaxLength = 150;
    public const int CertificateTitleMaxLength = 200;
    public const int IssuerMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    // Har bir ro'yxat uchun maksimal elementlar soni
    public const int MaxSocialLinks = 20;
    public const int MaxSkills = 50;
    public const int MaxExperiences = 50;
    public const int MaxEducations = 20;
    public const int MaxCertificates = 50;
}
