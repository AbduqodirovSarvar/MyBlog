using MyBlog.Domain.Common;

namespace MyBlog.Domain.Posts.Events;

/// <param name="IsFirstPublish">Birinchi marta nashr qilinganmi (qayta nashrda false).</param>
public sealed record PostPublishedDomainEvent(Guid PostId, Guid OwnerId, DateTimeOffset PublishedAt, bool IsFirstPublish)
    : IDomainEvent;

public sealed record PostDeletedDomainEvent(Guid PostId, Guid OwnerId) : IDomainEvent;
