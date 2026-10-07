using MyBlog.Domain.Common;

namespace MyBlog.Domain.Posts;

/// <summary>Postning o'zgarmas surati (title + kontent). Faqat yaratiladi va o'chiriladi.</summary>
public sealed class PostRevision : Entity, IAggregateRoot, IOwnedEntity
{
    private PostRevision() { }

    private PostRevision(Guid postId, Guid ownerId, int revisionNumber, RevisionKind kind, string title,
        PostContent content, DateTimeOffset createdAt)
    {
        PostId = postId;
        OwnerId = ownerId;
        RevisionNumber = revisionNumber;
        Kind = kind;
        Title = title;
        Content = content;
        CreatedAt = createdAt;
    }

    public Guid PostId { get; private set; }
    public Guid OwnerId { get; private set; }
    public int RevisionNumber { get; private set; }
    public RevisionKind Kind { get; private set; }
    public string Title { get; private set; } = null!;
    public PostContent Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <param name="number">Post bo'yicha ketma-ket raqam (1 dan boshlanadi), Application qatlamida hisoblanadi.</param>
    public static PostRevision Create(Post post, RevisionKind kind, int number, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        return new PostRevision(post.Id, post.OwnerId, number, kind, post.Title, post.Content.Copy(), now.ToUniversalTime());
    }

    /// <summary>Postga hali qo'llanmagan holat (masalan autosave): title va kontent alohida beriladi.</summary>
    public static PostRevision Create(Post post, RevisionKind kind, int number, string title, PostContent content,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        return new PostRevision(post.Id, post.OwnerId, number, kind, title.Trim(), content.Copy(), now.ToUniversalTime());
    }
}
