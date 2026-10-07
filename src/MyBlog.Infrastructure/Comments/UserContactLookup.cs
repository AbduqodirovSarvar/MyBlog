using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Comments;

/// <summary>Identity'dan faqat o'qish: tasdiqlangan va bloklanmagan foydalanuvchi email'i.</summary>
internal sealed class UserContactLookup(AppDbContext dbContext) : IUserContactLookup
{
    public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.EmailConfirmed && !u.IsBlocked)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
}
