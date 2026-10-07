using MyBlog.Domain.Common;
using MyBlog.Domain.Posts.Events;
using static MyBlog.Domain.Posts.PostConstraints;

namespace MyBlog.Domain.Posts;

/// <summary>
/// Blog posti. Muallif (OwnerId) faqat o'z postlarini boshqaradi.
/// LikeCount/DislikeCount/CommentCount/ViewCount repository'da ExecuteUpdate orqali atomar yangilanadi.
/// </summary>
public sealed class Post : SoftDeletableEntity, IAggregateRoot, IOwnedEntity
{
    private readonly List<PostTag> _tags = [];
    private readonly List<PostMedia> _media = [];

    private Post() { }

    private Post(Guid ownerId, string title, string slug, PostContent content, SeoMeta seo)
    {
        OwnerId = ownerId;
        Title = title;
        Slug = slug;
        Content = content;
        Seo = seo;
        Status = PostStatus.Draft;
        AllowComments = true;
    }

    public Guid OwnerId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Summary { get; private set; }
    public Guid? CoverMediaId { get; private set; }
    public PostStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int ReadingTimeMinutes { get; private set; }
    public long ViewCount { get; private set; }
    public bool IsFeatured { get; private set; }
    public bool AllowComments { get; private set; }
    public int LikeCount { get; private set; }
    public int DislikeCount { get; private set; }
    public int CommentCount { get; private set; }

    public PostContent Content { get; private set; } = null!;
    public SeoMeta Seo { get; private set; } = null!;

    public IReadOnlyCollection<PostTag> Tags => _tags.AsReadOnly();
    public IReadOnlyCollection<PostMedia> Media => _media.AsReadOnly();

    public bool IsPublished => Status == PostStatus.Published;

    public static Result<Post> Create(Guid ownerId, string title, string slug, PostContent content,
        int readingTimeMinutes, string? summary = null, Guid? categoryId = null, Guid? coverMediaId = null,
        SeoMeta? seo = null)
    {
        if (ownerId == Guid.Empty)
            return PostErrors.InvalidOwner;

        var validation = ValidateDetails(title, slug, summary);
        if (validation.IsFailure)
            return validation.Error;
        if (readingTimeMinutes < 0)
            return PostErrors.ReadingTimeInvalid;

        var post = new Post(ownerId, title.Trim(), slug.Trim(), content.Copy(), seo?.Copy() ?? SeoMeta.Empty)
        {
            Summary = DomainRules.TrimToNull(summary),
            CategoryId = NormalizeId(categoryId),
            CoverMediaId = NormalizeId(coverMediaId),
            ReadingTimeMinutes = readingTimeMinutes
        };

        return post;
    }

    public Result UpdateDetails(string title, string slug, string? summary, Guid? categoryId, Guid? coverMediaId,
        SeoMeta? seo)
    {
        var validation = ValidateDetails(title, slug, summary);
        if (validation.IsFailure)
            return validation;

        Title = title.Trim();
        Slug = slug.Trim();
        Summary = DomainRules.TrimToNull(summary);
        CategoryId = NormalizeId(categoryId);
        CoverMediaId = NormalizeId(coverMediaId);
        Seo = seo?.Copy() ?? SeoMeta.Empty;
        return Result.Success();
    }

    /// <summary>Kontentni almashtiradi va PostMedia ro'yxatini kontentdagi data-media-id'lar bilan sinxronlaydi.</summary>
    public Result UpdateContent(PostContent content, int readingTimeMinutes, IEnumerable<Guid> mediaIds)
    {
        if (readingTimeMinutes < 0)
            return PostErrors.ReadingTimeInvalid;

        Content = content.Copy();
        ReadingTimeMinutes = readingTimeMinutes;
        SyncMedia(mediaIds);
        return Result.Success();
    }

    public Result SetTags(IEnumerable<Guid> tagIds)
    {
        var ids = tagIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > MaxTags)
            return PostErrors.TooManyTags;

        _tags.RemoveAll(t => !ids.Contains(t.TagId));
        foreach (var id in ids.Where(id => !_tags.Exists(t => t.TagId == id)))
            _tags.Add(new PostTag(Id, id));

        return Result.Success();
    }

    /// <summary>Draft/Scheduled/Archived → Published. PublishedAt faqat birinchi nashrda qo'yiladi.</summary>
    public Result Publish(DateTimeOffset now)
    {
        if (Status == PostStatus.Published)
            return PostErrors.AlreadyPublished;

        var isFirstPublish = PublishedAt is null;
        PublishedAt ??= now.ToUniversalTime();
        Status = PostStatus.Published;
        ScheduledAt = null;

        Raise(new PostPublishedDomainEvent(Id, OwnerId, PublishedAt.Value, isFirstPublish));
        return Result.Success();
    }

    /// <summary>Published/Scheduled/Archived → Draft (rejalashtirish bekor qilinadi).</summary>
    public Result Unpublish()
    {
        if (Status == PostStatus.Draft)
            return PostErrors.NotPublished;

        Status = PostStatus.Draft;
        ScheduledAt = null;
        return Result.Success();
    }

    /// <summary>Kelajakdagi vaqtga rejalashtirish. Nashr qilingan postni avval Unpublish qilish kerak.</summary>
    public Result Schedule(DateTimeOffset at, DateTimeOffset now)
    {
        if (Status == PostStatus.Published)
            return PostErrors.CannotSchedulePublished;
        if (at <= now)
            return PostErrors.ScheduleInPast;

        Status = PostStatus.Scheduled;
        ScheduledAt = at.ToUniversalTime();
        return Result.Success();
    }

    /// <summary>Rejalashtirilgan vaqti kelganmi (fon vazifasi uchun).</summary>
    public bool IsDueForPublishing(DateTimeOffset now) =>
        Status == PostStatus.Scheduled && ScheduledAt is { } at && at <= now;

    public Result Archive()
    {
        if (Status == PostStatus.Archived)
            return PostErrors.AlreadyArchived;

        Status = PostStatus.Archived;
        ScheduledAt = null;
        return Result.Success();
    }

    public void Feature() => IsFeatured = true;

    public void Unfeature() => IsFeatured = false;

    public void SetAllowComments(bool allow) => AllowComments = allow;

    public override void MarkDeleted()
    {
        if (IsDeleted)
            return;

        base.MarkDeleted();
        Raise(new PostDeletedDomainEvent(Id, OwnerId));
    }

    public static bool IsValidSlug(string? slug) => slug is { Length: <= SlugMaxLength } && DomainRules.IsValidSlug(slug);

    private void SyncMedia(IEnumerable<Guid> mediaIds)
    {
        var ids = mediaIds.Where(id => id != Guid.Empty).Distinct().ToList();

        _media.RemoveAll(m => !ids.Contains(m.MediaFileId));
        foreach (var id in ids.Where(id => !_media.Exists(m => m.MediaFileId == id)))
            _media.Add(new PostMedia(Id, id));
    }

    private static Result ValidateDetails(string? title, string? slug, string? summary)
    {
        var titleValue = DomainRules.TrimToNull(title);
        if (titleValue is null)
            return PostErrors.TitleRequired;
        if (titleValue.Length > TitleMaxLength)
            return PostErrors.TitleTooLong;
        if (!IsValidSlug(slug?.Trim()))
            return PostErrors.SlugInvalid;
        if (DomainRules.TrimToNull(summary)?.Length > SummaryMaxLength)
            return PostErrors.SummaryTooLong;

        return Result.Success();
    }

    private static Guid? NormalizeId(Guid? id) => id == Guid.Empty ? null : id;
}
