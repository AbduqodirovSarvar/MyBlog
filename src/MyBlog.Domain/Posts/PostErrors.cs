using MyBlog.Domain.Common;
using static MyBlog.Domain.Posts.PostConstraints;

namespace MyBlog.Domain.Posts;

public static class PostErrors
{
    public static readonly Error NotFound = Error.NotFound("Post.NotFound", "Post was not found.");
    public static readonly Error InvalidOwner = Error.Validation("Post.InvalidOwner", "Post author is required.");
    public static readonly Error TitleRequired = Error.Validation("Post.TitleRequired", "Title is required.");

    public static readonly Error TitleTooLong = Error.Validation("Post.TitleTooLong", "Title must not exceed {0} characters.")
        .WithArgs(TitleMaxLength);

    public static readonly Error SlugInvalid = Error.Validation("Post.SlugInvalid",
            "Slug must contain only lowercase latin letters, digits and hyphens and must not exceed {0} characters.")
        .WithArgs(SlugMaxLength);

    public static readonly Error SlugTaken = Error.Conflict("Post.SlugTaken", "A post with the same slug already exists.");

    public static readonly Error SummaryTooLong = Error.Validation("Post.SummaryTooLong", "Summary must not exceed {0} characters.")
        .WithArgs(SummaryMaxLength);

    public static readonly Error ReadingTimeInvalid = Error.Validation("Post.ReadingTimeInvalid", "Reading time cannot be negative.");

    public static readonly Error TooManyTags = Error.Validation("Post.TooManyTags", "A post cannot have more than {0} tags.")
        .WithArgs(MaxTags);

    // Kontent
    public static readonly Error ContentFormatRequired = Error.Validation("Post.ContentFormatRequired", "Content format is required.");

    public static readonly Error ContentFormatTooLong = Error.Validation("Post.ContentFormatTooLong", "Content format must not exceed {0} characters.")
        .WithArgs(ContentFormatMaxLength);

    public static readonly Error ContentTooLong = Error.Validation("Post.ContentTooLong", "Content is too long.");

    // SEO
    public static readonly Error MetaTitleTooLong = Error.Validation("Post.MetaTitleTooLong", "Meta title must not exceed {0} characters.")
        .WithArgs(MetaTitleMaxLength);

    public static readonly Error MetaDescriptionTooLong = Error.Validation("Post.MetaDescriptionTooLong", "Meta description must not exceed {0} characters.")
        .WithArgs(MetaDescriptionMaxLength);

    public static readonly Error CanonicalUrlInvalid = Error.Validation("Post.CanonicalUrlInvalid", "Canonical URL must be a valid http(s) URL.");

    // Holat o'tishlari
    public static readonly Error AlreadyPublished = Error.Conflict("Post.AlreadyPublished", "Post is already published.");
    public static readonly Error NotPublished = Error.Conflict("Post.NotPublished", "Post is a draft already.");
    public static readonly Error AlreadyArchived = Error.Conflict("Post.AlreadyArchived", "Post is already archived.");
    public static readonly Error ScheduleInPast = Error.Validation("Post.ScheduleInPast", "Scheduled time must be in the future.");
    public static readonly Error CannotSchedulePublished = Error.Conflict("Post.CannotSchedulePublished", "A published post cannot be scheduled. Unpublish it first.");
    public static readonly Error CommentsDisabled = Error.Forbidden("Post.CommentsDisabled", "Comments are disabled for this post.");
    public static readonly Error NotPublic = Error.NotFound("Post.NotPublic", "Post is not published.");

    // Reviziyalar
    public static readonly Error RevisionNotFound = Error.NotFound("Post.RevisionNotFound", "Post revision was not found.");
}
