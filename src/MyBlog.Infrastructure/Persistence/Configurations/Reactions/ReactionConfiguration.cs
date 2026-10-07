using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Domain.Reactions;
using MyBlog.Domain.Users;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Persistence.Configurations.Reactions;

internal sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("reactions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TargetType).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();

        // Bitta foydalanuvchi — bitta target uchun bitta reaksiya
        builder.HasIndex(r => new { r.UserId, r.TargetType, r.TargetId }).IsUnique();
        builder.HasIndex(r => new { r.TargetType, r.TargetId });

        // Target polimorf (post yoki izoh) — FK yo'q
        builder.HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
