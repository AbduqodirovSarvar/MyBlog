namespace MyBlog.Application.Features.Comments.Common;

public sealed record CommentAuthorDto(Guid Id, string Username, string DisplayName, string? AvatarUrl);

/// <summary>
/// Izoh (javoblari bilan). O'chirilgan, lekin javoblari bor izoh placeholder sifatida qaytadi:
/// Deleted = true, Content va Author = null.
/// </summary>
/// <param name="MyReaction">Joriy foydalanuvchining reaksiyasi: "Like", "Dislike" yoki null.</param>
public sealed record CommentDto(
    Guid Id,
    Guid PostId,
    Guid? ParentId,
    int Depth,
    string? Content,
    CommentAuthorDto? Author,
    bool Deleted,
    bool IsEdited,
    DateTimeOffset? EditedAt,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int DislikeCount,
    string? MyReaction,
    bool CanEdit,
    bool CanDelete,
    bool CanReply,
    IReadOnlyList<CommentDto> Replies);

public sealed record CommentCountDto(Guid PostId, int Count);

public sealed record CommentPostRefDto(Guid Id, string Title, string Slug, string AuthorUsername);

/// <summary>"Mening izohlarim" va admin ro'yxati elementi.</summary>
public sealed record CommentListItemDto(
    Guid Id,
    Guid? ParentId,
    string Content,
    bool IsEdited,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int DislikeCount,
    CommentPostRefDto Post,
    CommentAuthorDto Author);
