using Microsoft.EntityFrameworkCore;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Identity;

namespace MyBlog.Api.IntegrationTests.Infrastructure;

internal static class TestUsers
{
    /// <summary>
    /// Identity user + unga bog'langan UserProfile (user_profiles.id → users.id FK sababli profil yolg'iz qo'shilolmaydi).
    /// SaveChanges chaqiruvchi tomonidan bajariladi.
    /// </summary>
    public static UserProfile AddUserWithProfile(this DbContext db, Guid id, string username)
    {
        var email = $"{username}@test.local";
        db.Add(new ApplicationUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var profile = UserProfile.Create(id, username).Value;
        db.Add(profile);
        return profile;
    }
}
