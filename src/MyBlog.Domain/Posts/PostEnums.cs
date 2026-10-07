namespace MyBlog.Domain.Posts;

public enum PostStatus
{
    Draft = 0,
    Scheduled = 1,
    Published = 2,
    Archived = 3
}

public enum RevisionKind
{
    Manual = 0,
    Autosave = 1,
    Publish = 2
}
