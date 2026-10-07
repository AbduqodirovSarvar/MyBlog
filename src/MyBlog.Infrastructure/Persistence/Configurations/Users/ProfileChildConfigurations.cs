using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Users;

// Child Id'lar domain'da (Guid v7) yaratiladi. ValueGeneratedNever bo'lmasa EF kolleksiyaga qo'shilgan
// yangi child'ni "Modified" deb hisoblab UPDATE qiladi — shuning uchun hamma joyda ValueGeneratedNever.

internal sealed class SocialLinkConfiguration : IEntityTypeConfiguration<SocialLink>
{
    public void Configure(EntityTypeBuilder<SocialLink> builder)
    {
        builder.ToTable("user_social_links");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Platform).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();
        builder.Property(l => l.Url).HasMaxLength(UrlMaxLength).IsRequired();

        builder.HasIndex(l => new { l.ProfileId, l.Order });
    }
}

internal sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("user_skills", t => t.HasCheckConstraint(
            "ck_user_skills_level",
            $"\"level\" IS NULL OR (\"level\" >= {SkillLevelMin} AND \"level\" <= {SkillLevelMax})"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).HasMaxLength(SkillNameMaxLength).IsRequired();

        builder.HasIndex(s => new { s.ProfileId, s.Order });
    }
}

internal sealed class ExperienceConfiguration : IEntityTypeConfiguration<Experience>
{
    public void Configure(EntityTypeBuilder<Experience> builder)
    {
        builder.ToTable("user_experiences");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Company).HasMaxLength(CompanyMaxLength).IsRequired();
        builder.Property(e => e.Position).HasMaxLength(PositionMaxLength).IsRequired();
        builder.Property(e => e.Location).HasMaxLength(LocationMaxLength);
        builder.Property(e => e.Description).HasMaxLength(DescriptionMaxLength);

        builder.HasIndex(e => e.ProfileId);
    }
}

internal sealed class EducationConfiguration : IEntityTypeConfiguration<Education>
{
    public void Configure(EntityTypeBuilder<Education> builder)
    {
        builder.ToTable("user_educations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Institution).HasMaxLength(InstitutionMaxLength).IsRequired();
        builder.Property(e => e.Degree).HasMaxLength(DegreeMaxLength);
        builder.Property(e => e.FieldOfStudy).HasMaxLength(FieldOfStudyMaxLength);
        builder.Property(e => e.Description).HasMaxLength(DescriptionMaxLength);

        builder.HasIndex(e => e.ProfileId);
    }
}

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("user_certificates");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Title).HasMaxLength(CertificateTitleMaxLength).IsRequired();
        builder.Property(c => c.Issuer).HasMaxLength(IssuerMaxLength);
        builder.Property(c => c.CredentialUrl).HasMaxLength(UrlMaxLength);

        builder.HasIndex(c => c.ProfileId);
    }
}
