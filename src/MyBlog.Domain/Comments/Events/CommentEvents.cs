using MyBlog.Domain.Common;

namespace MyBlog.Domain.Comments.Events;

/// <param name="ParentAuthorId">Javob bo'lsa ota izoh muallifi (bildirishnoma uchun).</param>
public sealed record CommentCreatedDomainEvent(Guid CommentId, Guid PostId, Guid AuthorId, Guid? ParentId, Guid? ParentAuthorId)
    : IDomainEvent;

public sealed record CommentDeletedDomainEvent(Guid CommentId, Guid PostId, Guid AuthorId) : IDomainEvent;
