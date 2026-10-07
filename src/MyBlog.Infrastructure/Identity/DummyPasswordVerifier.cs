using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace MyBlog.Infrastructure.Identity;

/// <summary>
/// Login'da foydalanuvchi topilmaganda ham xuddi shu <see cref="IPasswordHasher{TUser}"/> bilan bitta parol xeshi
/// tekshiriladi — javob vaqti foydalanuvchi mavjudligini oshkor qilmaydi. Soxta xesh hasher turi bo'yicha bir marta
/// hisoblanadi (har so'rovda qayta hisoblansa vaqt yana farqlanib qolardi).
/// </summary>
internal sealed class DummyPasswordVerifier(IPasswordHasher<ApplicationUser> passwordHasher)
{
    private static readonly ConcurrentDictionary<Type, string> DummyHashes = new();
    private static readonly ApplicationUser DummyUser = new() { Id = Guid.Empty, UserName = "dummy" };

    public void Verify(string password)
    {
        var hash = DummyHashes.GetOrAdd(passwordHasher.GetType(),
            static (_, hasher) => hasher.HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            passwordHasher);

        // Natija ahamiyatsiz — faqat sarflangan vaqt muhim.
        _ = passwordHasher.VerifyHashedPassword(DummyUser, hash, password);
    }
}
