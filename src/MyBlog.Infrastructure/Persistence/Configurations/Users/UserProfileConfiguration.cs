using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        builder.HasKey(p => p.Id);
        // Id == identity user id (domain beradi)
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Username).HasMaxLength(UsernameMaxLength).IsRequired();
        builder.Property(p => p.FirstName).HasMaxLength(FirstNameMaxLength);
        builder.Property(p => p.LastName).HasMaxLength(LastNameMaxLength);
        builder.Property(p => p.DisplayName).HasMaxLength(DisplayNameMaxLength).IsRequired();
        builder.Property(p => p.Bio).HasMaxLength(BioMaxLength);
        builder.Property(p => p.AboutMe).HasMaxLength(AboutMeMaxLength);
        builder.Property(p => p.Location).HasMaxLength(LocationMaxLength);
        builder.Property(p => p.Profession).HasMaxLength(ProfessionMaxLength);
        builder.Property(p => p.Company).HasMaxLength(CompanyMaxLength);
        builder.Property(p => p.Website).HasMaxLength(UrlMaxLength);
        builder.Property(p => p.PublicEmail).HasMaxLength(EmailMaxLength);
        builder.Property(p => p.Phone).HasMaxLength(PhoneMaxLength);
        builder.Property(p => p.PreferredCulture).HasMaxLength(Cultures.MaxLength).IsRequired();

        builder.HasIndex(p => p.Username).IsUnique();

        // Child'lar faqat aggregate orqali; 5 ta kolleksiya bo'lgani uchun AutoInclude qilinmaydi
        // (cartesian explosion) — repository kerakli Include'larni (AsSplitQuery bilan) o'zi qo'shadi.
        builder.HasMany(p => p.SocialLinks).WithOne().HasForeignKey(l => l.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Skills).WithOne().HasForeignKey(s => s.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Experiences).WithOne().HasForeignKey(e => e.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Educations).WithOne().HasForeignKey(e => e.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Certificates).WithOne().HasForeignKey(c => c.ProfileId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.SocialLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Skills).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Experiences).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Educations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Certificates).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
