using MyBlog.Domain.Common;
using MyBlog.Domain.Comments.Events;
using static MyBlog.Domain.Comments.CommentConstraints;

namespace MyBlog.Domain.Comments;

/// <summary>
/// Post ostidagi izoh. Darhol nashr qilinadi (moderatsiya yo'q), muallif har doim ko'rinadi.
/// Javoblar daraxti: Depth 0 (ildiz) .. MaxDepth-1. Owned emas — ommaviy o'qiladi.
/// </summary>
public sealed class Comment : SoftDeletableEntity, IAggregateRoot
{
    private Comment() { }

    private Comment(Guid postId, Guid authorId, Guid? parentId, int depth, string content)
    {
        PostId = postId;
        AuthorId = authorId;
        ParentId = parentId;
        Depth = depth;
        Content = content;
    }

    public Guid PostId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid? ParentId { get; private set; }
    public int Depth { get; private set; }

    /// <summary>Plain text.</summary>
    public string Content { get; private set; } = null!;

    public bool IsEdited { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public int LikeCount { get; private set; }
    public int DislikeCount { get; private set; }

    /// <param name="parent">Javob bo'lsa ota izoh (repository'dan yuklangan). Post'da izohlarga ruxsat borligi Application'da tekshiriladi.</param>
    public static Result<Comment> Create(Guid postId, Guid authorId, string content, Comment? parent = null)
    {
        if (postId == Guid.Empty)
            return CommentErrors.InvalidPost;
        if (authorId == Guid.Empty)
            return CommentErrors.InvalidAuthor;

        var contentResult = NormalizeContent(content);
        if (contentResult.IsFailure)
            return contentResult.Error;

        var depth = 0;
        if (parent is not null)
        {
            if (parent.PostId != postId)
                return CommentErrors.ParentFromAnotherPost;
            if (parent.IsDeleted)
                return CommentErrors.ParentDeleted;

            depth = parent.Depth + 1;
            if (depth >= MaxDepth)
                return CommentErrors.MaxDepthExceeded;
        }

        var comment = new Comment(postId, authorId, parent?.Id, depth, contentResult.Value);
        comment.Raise(new CommentCreatedDomainEvent(comment.Id, postId, authorId, parent?.Id, parent?.AuthorId));
        return comment;
    }

    public bool IsAuthoredBy(Guid userId) => AuthorId == userId;

    public Result Edit(string content, DateTimeOffset now)
    {
        if (IsDeleted)
            return CommentErrors.Deleted;

        var contentResult = NormalizeContent(content);
        if (contentResult.IsFailure)
            return contentResult.Error;

        if (contentResult.Value == Content)
            return Result.Success();

        Content = contentResult.Value;
        IsEdited = true;
        EditedAt = now.ToUniversalTime();
        return Result.Success();
    }

    public override void MarkDeleted()
    {
        if (IsDeleted)
            return;

        base.MarkDeleted();
        Raise(new CommentDeletedDomainEvent(Id, PostId, AuthorId));
    }

    private static Result<string> NormalizeContent(string? content)
    {
        var value = DomainRules.TrimToNull(content);
        if (value is null)
            return CommentErrors.ContentRequired;
        if (value.Length > ContentMaxLength)
            return CommentErrors.ContentTooLong;

        return value;
    }
}

public static class CommentConstraints
{
    public const int ContentMaxLength = 5000;

    /// <summary>Daraxt darajalari soni: Depth 0, 1, 2.</summary>
    public const int MaxDepth = 3;
}

public static class CommentErrors
{
    public static readonly Error NotFound = Error.NotFound("Comment.NotFound", "Comment was not found.");
    public static readonly Error InvalidPost = Error.Validation("Comment.InvalidPost", "Post is required.");
    public static readonly Error InvalidAuthor = Error.Validation("Comment.InvalidAuthor", "Comment author is required.");
    public static readonly Error ContentRequired = Error.Validation("Comment.ContentRequired", "Comment text is required.");

    public static readonly Error ContentTooLong = Error.Validation("Comment.ContentTooLong", "Comment must not exceed {0} characters.")
        .WithArgs(CommentConstraints.ContentMaxLength);

    public static readonly Error MaxDepthExceeded = Error.Validation("Comment.MaxDepthExceeded", "Replies cannot be nested deeper than {0} levels.")
        .WithArgs(CommentConstraints.MaxDepth);

    public static readonly Error ParentNotFound = Error.NotFound("Comment.ParentNotFound", "Parent comment was not found.");
    public static readonly Error ParentFromAnotherPost = Error.Validation("Comment.ParentFromAnotherPost", "Parent comment belongs to another post.");
    public static readonly Error ParentDeleted = Error.Conflict("Comment.ParentDeleted", "Cannot reply to a deleted comment.");
    public static readonly Error Deleted = Error.Conflict("Comment.Deleted", "Comment has been deleted.");
    public static readonly Error NotAuthor = Error.Forbidden("Comment.NotAuthor", "Only the author can modify this comment.");

    public static readonly Error PostNotFound = Error.NotFound("Comment.PostNotFound", "Post was not found or is not published.");
    public static readonly Error CommentsDisabled = Error.Forbidden("Comment.CommentsDisabled", "Comments are disabled for this post.");

    public static readonly Error EditWindowExpired = Error.Forbidden("Comment.EditWindowExpired", "Comments can only be edited within {0} minutes.");

    public static readonly Error DeleteForbidden = Error.Forbidden("Comment.DeleteForbidden", "You are not allowed to delete this comment.");
}
