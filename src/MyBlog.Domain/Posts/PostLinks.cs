namespace MyBlog.Domain.Posts;

/// <summary>Post ↔ Tag bog'lanishi. Kalit: (PostId, TagId).</summary>
public sealed class PostTag
{
    private PostTag() { }

    internal PostTag(Guid postId, Guid tagId)
    {
        PostId = postId;
        TagId = tagId;
    }

    public Guid PostId { get; private set; }
    public Guid TagId { get; private set; }
}

/// <summary>Post kontentida ishlatilgan media (data-media-id). Kalit: (PostId, MediaFileId).</summary>
public sealed class PostMedia
{
    private PostMedia() { }

    internal PostMedia(Guid postId, Guid mediaFileId)
    {
        PostId = postId;
        MediaFileId = mediaFileId;
    }

    public Guid PostId { get; private set; }
    public Guid MediaFileId { get; private set; }
}
