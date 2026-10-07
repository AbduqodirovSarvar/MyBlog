using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;

namespace MyBlog.Infrastructure.Persistence.Repositories;

/// <summary>
/// "Ishlatilmoqda" tekshiruvi barcha egalar bo'yicha (IgnoreQueryFilters butun so'rovga, shu jumladan subquery'larga
/// ta'sir qiladi), soft delete esa qo'lda hisobga olinadi: o'chirilgan post/kategoriya havolasi media'ni ushlab turmaydi.
/// </summary>
internal sealed class MediaRepository(AppDbContext dbContext) : EfRepository<MediaFile>(dbContext), IMediaRepository
{
    public Task<bool> IsReferencedAsync(Guid mediaId, CancellationToken cancellationToken = default) =>
        AllMedia().Where(m => m.Id == mediaId).AnyAsync(Not(IsUnreferenced()), cancellationToken);

    public async Task<IReadOnlyList<MediaFile>> ListUnreferencedAsync(DateTimeOffset createdBefore, int take,
        CancellationToken cancellationToken = default) =>
        await UnreferencedQuery(createdBefore, take).ToListAsync(cancellationToken);

    public async Task<bool> DeleteIfUnreferencedAsync(Guid mediaId, CancellationToken cancellationToken = default) =>
        await AllMedia()
            .Where(m => m.Id == mediaId)
            .Where(IsUnreferenced())
            .ExecuteDeleteAsync(cancellationToken) > 0;

    internal IQueryable<MediaFile> UnreferencedQuery(DateTimeOffset createdBefore, int take) =>
        AllMedia()
            .AsNoTracking()
            .Where(m => m.CreatedAt < createdBefore)
            .Where(IsUnreferenced())
            .OrderBy(m => m.CreatedAt)
            .Take(take);

    private IQueryable<MediaFile> AllMedia() => Set.IgnoreQueryFilters();

    private Expression<Func<MediaFile, bool>> IsUnreferenced()
    {
        var postMedia = DbContext.Set<PostMedia>();
        var posts = DbContext.Set<Post>();
        var profiles = DbContext.Set<UserProfile>();
        var categories = DbContext.Set<Category>();
        var certificates = DbContext.Set<Certificate>();

        return m =>
            !postMedia.Any(pm => pm.MediaFileId == m.Id && posts.Any(p => p.Id == pm.PostId && !p.IsDeleted))
            && !posts.Any(p => !p.IsDeleted && (p.CoverMediaId == m.Id || p.Seo.OgImageMediaId == m.Id))
            && !profiles.Any(u => u.AvatarMediaId == m.Id || u.CoverMediaId == m.Id)
            && !categories.Any(c => !c.IsDeleted && c.IconMediaId == m.Id)
            && !certificates.Any(c => c.MediaId == m.Id);
    }

    private static Expression<Func<MediaFile, bool>> Not(Expression<Func<MediaFile, bool>> predicate) =>
        Expression.Lambda<Func<MediaFile, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
}
