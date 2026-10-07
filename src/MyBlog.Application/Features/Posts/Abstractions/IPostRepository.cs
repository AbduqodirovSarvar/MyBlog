using MyBlog.Application.Common.Models;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Posts.Abstractions;

public enum PublicPostSort
{
    Newest = 0,
    Popular = 1,
    MostLiked = 2,

    /// <summary>Faqat qidiruv (q) bilan: ts_rank bo'yicha.</summary>
    Relevance = 3
}

/// <summary>Public ro'yxat filtri. Slug'lar Application qatlamida id'larga aylantirilgan bo'ladi.</summary>
public sealed record PublicPostFilter(
    Guid? AuthorId = null,
    IReadOnlyCollection<Guid>? CategoryIds = null,
    IReadOnlyCollection<Guid>? TagIds = null,
    string? Search = null,
    bool? Featured = null,
    PublicPostSort Sort = PublicPostSort.Newest);

public sealed record PublicPostRow(
    Guid Id,
    Guid OwnerId,
    Guid? CategoryId,
    Guid? CoverMediaId,
    string Title,
    string Slug,
    string? Summary,
    DateTimeOffset? PublishedAt,
    int ReadingTimeMinutes,
    bool IsFeatured,
    long ViewCount,
    int LikeCount,
    int DislikeCount,
    int CommentCount,
    IReadOnlyList<Guid> TagIds);

/// <summary>Post uchun EF'ga xos amallar: xmin versiyasi, atomar hisoblagich, PostgreSQL full-text qidiruv.</summary>
public interface IPostRepository
{
    /// <summary>Kuzatilayotgan post'ning joriy versiyasi (PostgreSQL xmin).</summary>
    uint GetVersion(Post post);

    /// <summary>Mijoz ko'rgan versiya: SaveChanges UPDATE ... WHERE xmin = version qiladi (mos kelmasa 409).</summary>
    void SetExpectedVersion(Post post, uint version);

    /// <summary>ViewCount'ni bitta UPDATE bilan oshiradi (ownership filtrisiz).</summary>
    Task IncrementViewCountAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>Reviziyalarni darhol (ExecuteDelete) o'chiradi — tranzaksiya ichida chaqirilishi kerak.</summary>
    Task DeleteRevisionsAsync(IReadOnlyCollection<Guid> revisionIds, CancellationToken cancellationToken = default);

    /// <summary>Faqat Published va o'chirilmagan postlar; q bo'lsa plainto_tsquery('simple', q) + ts_rank.</summary>
    Task<PagedList<PublicPostRow>> ListPublishedAsync(PublicPostFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
