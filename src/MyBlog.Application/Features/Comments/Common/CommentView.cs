using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Reactions;

namespace MyBlog.Application.Features.Comments.Common;

/// <summary>Izohni joriy foydalanuvchi nuqtai nazaridan qaysi amallar mumkinligi.</summary>
internal static class CommentRules
{
    public static bool CanModerate(ICurrentUser user) => user.HasPermission(Permissions.Comments.Moderate);

    /// <summary>Muallif, post egasi yoki moderator.</summary>
    public static bool CanDelete(Comment comment, Guid? userId, Guid postOwnerId, bool canModerate) =>
        !comment.IsDeleted && userId is { } id && (comment.IsAuthoredBy(id) || postOwnerId == id || canModerate);

    public static bool IsWithinEditWindow(Comment comment, CommentsOptions options, DateTimeOffset now) =>
        options.EditWindowMinutes <= 0 || now - comment.CreatedAt <= TimeSpan.FromMinutes(options.EditWindowMinutes);

    public static bool CanEdit(Comment comment, Guid? userId, CommentsOptions options, DateTimeOffset now) =>
        !comment.IsDeleted && userId is { } id && comment.IsAuthoredBy(id) && IsWithinEditWindow(comment, options, now);

    public static bool CanReply(Comment comment, CommentPostInfo post, bool canWrite, CommentsOptions options) =>
        !comment.IsDeleted && canWrite && post.IsPublic && post.AllowComments && comment.Depth + 1 < options.MaxDepth;
}

/// <summary>
/// Izohlarni DTO'ga aylantirish uchun kerakli ma'lumotlar: mualliflar, joriy foydalanuvchi reaksiyalari, ruxsatlar.
/// Barcha ma'lumot bir martada (har biri bitta so'rov bilan) yuklanadi.
/// </summary>
internal sealed class CommentView
{
    private readonly CommentPostInfo _post;
    private readonly Guid? _userId;
    private readonly bool _canModerate;
    private readonly bool _canWrite;
    private readonly CommentsOptions _options;
    private readonly DateTimeOffset _now;
    private readonly IReadOnlyDictionary<Guid, CommentUserInfo> _users;
    private readonly IReadOnlyDictionary<Guid, ReactionType> _myReactions;
    private readonly IFileStorage _fileStorage;

    private CommentView(CommentPostInfo post, ICurrentUser currentUser, CommentsOptions options, DateTimeOffset now,
        IReadOnlyDictionary<Guid, CommentUserInfo> users, IReadOnlyDictionary<Guid, ReactionType> myReactions,
        IFileStorage fileStorage)
    {
        _post = post;
        _userId = currentUser.Id;
        _canModerate = CommentRules.CanModerate(currentUser);
        _canWrite = currentUser.HasPermission(Permissions.Comments.Write);
        _options = options;
        _now = now;
        _users = users;
        _myReactions = myReactions;
        _fileStorage = fileStorage;
    }

    public static async Task<CommentView> LoadAsync(
        CommentPostInfo post,
        IReadOnlyCollection<Comment> comments,
        ICommentRepository commentRepository,
        IReactionRepository reactionRepository,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        CommentsOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var visible = comments.Where(c => !c.IsDeleted).ToList();

        var authorIds = visible.Select(c => c.AuthorId).Distinct().ToList();
        var users = authorIds.Count == 0
            ? new Dictionary<Guid, CommentUserInfo>()
            : await commentRepository.GetUsersAsync(authorIds, cancellationToken);

        IReadOnlyDictionary<Guid, ReactionType> myReactions = new Dictionary<Guid, ReactionType>();
        if (currentUser.Id is { } userId && visible.Count > 0)
            myReactions = await reactionRepository.GetUserReactionsAsync(
                userId, ReactionTargetType.Comment, visible.Select(c => c.Id).ToList(), cancellationToken);

        return new CommentView(post, currentUser, options, now, users, myReactions, fileStorage);
    }

    /// <summary>
    /// Ildizlar va ularning avlodlaridan daraxt quradi. Javoblar eskisi birinchi. O'chirilgan izoh ko'rinadigan
    /// avlodi bo'lsa placeholder bo'lib qoladi, aks holda (barg) tashlab yuboriladi.
    /// </summary>
    public List<CommentDto> BuildTree(IEnumerable<Comment> roots, IEnumerable<Comment> replies)
    {
        var children = replies.ToLookup(r => r.ParentId);

        CommentDto? Build(Comment comment)
        {
            var kids = children[comment.Id]
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Select(Build)
                .OfType<CommentDto>()
                .ToList();

            return comment.IsDeleted && kids.Count == 0 ? null : Map(comment, kids);
        }

        return roots.Select(Build).OfType<CommentDto>().ToList();
    }

    public CommentDto Map(Comment comment, IReadOnlyList<CommentDto>? replies = null)
    {
        replies ??= [];

        if (comment.IsDeleted)
        {
            return new CommentDto(comment.Id, comment.PostId, comment.ParentId, comment.Depth, null, null, Deleted: true,
                IsEdited: false, EditedAt: null, comment.CreatedAt, 0, 0, MyReaction: null,
                CanEdit: false, CanDelete: false, CanReply: false, replies);
        }

        return new CommentDto(
            comment.Id,
            comment.PostId,
            comment.ParentId,
            comment.Depth,
            comment.Content,
            MapAuthor(comment.AuthorId),
            Deleted: false,
            comment.IsEdited,
            comment.EditedAt,
            comment.CreatedAt,
            comment.LikeCount,
            comment.DislikeCount,
            _myReactions.TryGetValue(comment.Id, out var reaction) ? reaction.ToString() : null,
            CommentRules.CanEdit(comment, _userId, _options, _now),
            CommentRules.CanDelete(comment, _userId, _post.OwnerId, _canModerate),
            CommentRules.CanReply(comment, _post, _canWrite, _options),
            replies);
    }

    private CommentAuthorDto? MapAuthor(Guid authorId) =>
        _users.TryGetValue(authorId, out var user) ? ToAuthorDto(user, _fileStorage) : null;

    public static CommentAuthorDto ToAuthorDto(CommentUserInfo user, IFileStorage fileStorage) =>
        new(user.Id, user.Username, user.DisplayName,
            user.AvatarStorageKey is { } key ? fileStorage.GetPublicUrl(key) : null);
}
