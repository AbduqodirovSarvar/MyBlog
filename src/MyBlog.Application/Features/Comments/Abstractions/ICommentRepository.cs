using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Common.Models;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Comments.Abstractions;

public enum CommentSort
{
    Oldest = 0,
    Newest = 1,
    Top = 2
}

/// <summary>Izoh qaysi postga tegishli — ruxsat va havolalar uchun (ownership filtri hisobga olinmaydi).</summary>
public sealed record CommentPostInfo(
    Guid Id,
    Guid OwnerId,
    string OwnerUsername,
    string Title,
    string Slug,
    PostStatus Status,
    bool AllowComments,
    bool IsDeleted,
    int CommentCount)
{
    public bool IsPublic => Status == PostStatus.Published && !IsDeleted;
}

/// <summary>Izoh muallifi / bildirishnoma oluvchisi haqida ommaviy ma'lumot.</summary>
public sealed record CommentUserInfo(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarStorageKey,
    string PreferredCulture,
    bool NotifyOnComment,
    bool NotifyOnReply);

/// <summary>"Mening izohlarim" va admin ro'yxati uchun tekis qator.</summary>
public sealed record CommentListRow(
    Guid Id,
    Guid PostId,
    Guid? ParentId,
    string Content,
    bool IsEdited,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int DislikeCount,
    string PostTitle,
    string PostSlug,
    string PostAuthorUsername,
    Guid AuthorId,
    string AuthorUsername,
    string AuthorDisplayName);

/// <param name="AuthorId">Berilsa faqat shu muallifning izohlari.</param>
/// <param name="Search">Matn yoki muallif username'i bo'yicha (ILIKE).</param>
public sealed record CommentListFilter(Guid? AuthorId = null, string? Search = null);

public interface ICommentRepository : IRepository<Comment>
{
    /// <summary>O'chirilganlarini ham qaytaradi (tracked).</summary>
    Task<Comment?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CommentPostInfo?> GetPostInfoAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Post'ning ildiz izohlari sahifasi. O'chirilgan ildiz faqat o'chirilmagan avlodi bo'lsa qaytadi (placeholder uchun).
    /// </summary>
    Task<PagedList<Comment>> GetRootPageAsync(Guid postId, CommentSort sort, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Berilgan ildizlarning barcha avlodlari (Depth 1..2, o'chirilganlari bilan), eskisi birinchi.</summary>
    Task<List<Comment>> GetRepliesAsync(Guid postId, IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, CommentUserInfo>> GetUsersAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Post.CommentCount'ni atomar o'zgartiradi (ExecuteUpdate, manfiy bo'lmaydi).</summary>
    Task AdjustPostCommentCountAsync(Guid postId, int delta, CancellationToken cancellationToken = default);

    Task<PagedList<CommentListRow>> ListAsync(CommentListFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
