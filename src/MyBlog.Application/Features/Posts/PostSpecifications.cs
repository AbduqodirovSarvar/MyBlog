using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Posts;

// ---------- Post (boshqaruv: ownership filtri bilan) ----------

/// <summary>Joriy foydalanuvchining posti (Tags/Media avtomatik yuklanadi).</summary>
internal sealed class MyPostByIdSpec : Specification<Post>
{
    public MyPostByIdSpec(Guid id, bool tracked = true)
    {
        Where(p => p.Id == id);
        if (!tracked)
            ReadOnly();
    }
}

/// <summary>Egasining shu slug bilan boshlanadigan slug'lari (unikal slug tanlash uchun).</summary>
internal sealed class PostSlugsSpec : Specification<Post, string>
{
    public PostSlugsSpec(Guid ownerId, string baseSlug, Guid? exceptPostId)
    {
        var prefix = baseSlug + "-";
        Where(p => p.OwnerId == ownerId && (p.Slug == baseSlug || p.Slug.StartsWith(prefix)));
        if (exceptPostId is { } except)
            Where(p => p.Id != except);
        IgnoreOwnership();
        Select(p => p.Slug);
    }
}

internal sealed record MyPostListRow(
    Guid Id, string Title, string Slug, PostStatus Status, Guid? CategoryId, Guid? CoverMediaId, bool IsFeatured,
    int ReadingTimeMinutes, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledAt, long ViewCount, int LikeCount, int DislikeCount, int CommentCount);

public enum MyPostSort
{
    Updated = 0,
    Created = 1,
    Published = 2,
    Title = 3
}

internal sealed class MyPostsSpec : Specification<Post, MyPostListRow>
{
    public MyPostsSpec(PostStatus? status, Guid? categoryId, Guid? tagId, string? search, MyPostSort sort, bool descending,
        int page, int pageSize, bool forCount = false)
    {
        if (status is { } s)
            Where(p => p.Status == s);
        if (categoryId is { } c)
            Where(p => p.CategoryId == c);
        if (tagId is { } t)
            Where(p => p.Tags.Any(pt => pt.TagId == t));
        if (search?.Trim().ToLower() is { Length: > 0 } term)
            Where(p => p.Title.ToLower().Contains(term) || (p.Summary != null && p.Summary.ToLower().Contains(term)));

        Select(p => new MyPostListRow(p.Id, p.Title, p.Slug, p.Status, p.CategoryId, p.CoverMediaId, p.IsFeatured,
            p.ReadingTimeMinutes, p.CreatedAt, p.UpdatedAt, p.PublishedAt, p.ScheduledAt, p.ViewCount, p.LikeCount,
            p.DislikeCount, p.CommentCount));

        if (forCount)
            return;

        switch (sort)
        {
            case MyPostSort.Created:
                AddOrder(p => p.CreatedAt, descending);
                break;
            case MyPostSort.Published:
                AddOrder(p => p.PublishedAt!, descending);
                break;
            case MyPostSort.Title:
                AddOrder(p => p.Title, descending);
                break;
            default:
                AddOrder(p => p.UpdatedAt ?? p.CreatedAt, descending);
                break;
        }

        OrderByDesc(p => p.Id);
        Paginate(page, pageSize);
        ReadOnly();
    }

    private void AddOrder(System.Linq.Expressions.Expression<Func<Post, object>> key, bool descending)
    {
        if (descending)
            OrderByDesc(key);
        else
            OrderByAsc(key);
    }
}

/// <summary>Faol (o'chirilmagan, IsActive) va joriy foydalanuvchiga tegishli kategoriya.</summary>
internal sealed class MyActiveCategorySpec : Specification<Category>
{
    public MyActiveCategorySpec(Guid id) => Where(c => c.Id == id && c.IsActive);
}

// ---------- Reviziyalar ----------

internal sealed record RevisionHeader(Guid Id, int Number, RevisionKind Kind, string Title, DateTimeOffset CreatedAt);

/// <summary>Post reviziyalarining sarlavhalari (kontentsiz), raqam bo'yicha o'sish tartibida.</summary>
internal sealed class RevisionHeadersSpec : Specification<PostRevision, RevisionHeader>
{
    public RevisionHeadersSpec(Guid postId, RevisionKind? kind = null)
    {
        Where(r => r.PostId == postId);
        if (kind is { } k)
            Where(r => r.Kind == k);
        OrderByAsc(r => r.RevisionNumber);
        Select(r => new RevisionHeader(r.Id, r.RevisionNumber, r.Kind, r.Title, r.CreatedAt));
    }
}

internal sealed class RevisionByIdSpec : Specification<PostRevision>
{
    public RevisionByIdSpec(Guid postId, Guid revisionId)
    {
        Where(r => r.PostId == postId && r.Id == revisionId);
        ReadOnly();
    }
}

// ---------- Public / boshqa modullar (ownership filtrisiz) ----------

internal sealed class TagsByIdsSpec : Specification<Tag>
{
    public TagsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Where(t => ids.Contains(t.Id));
        IgnoreOwnership();
        ReadOnly();
    }
}

internal sealed record AuthorRow(Guid Id, string Username, string DisplayName, Guid? AvatarMediaId);

internal sealed class AuthorsByIdsSpec : Specification<UserProfile, AuthorRow>
{
    public AuthorsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Where(u => ids.Contains(u.Id));
        IgnoreOwnership();
        Select(u => new AuthorRow(u.Id, u.Username, u.DisplayName, u.AvatarMediaId));
    }
}

internal sealed class AuthorIdByUsernameSpec : Specification<UserProfile, Guid>
{
    public AuthorIdByUsernameSpec(string username)
    {
        var value = username.Trim().ToLower();
        Where(u => u.Username.ToLower() == value);
        IgnoreOwnership();
        Select(u => u.Id);
    }
}

internal sealed class CategoriesByIdsSpec : Specification<Category>
{
    public CategoriesByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Where(c => ids.Contains(c.Id));
        IgnoreOwnership();
        ReadOnly();
    }
}

internal sealed record CategoryNode(Guid Id, Guid? ParentId, Guid OwnerId);

/// <summary>Slug bo'yicha kategoriyalar (muallif berilsa — faqat uniki).</summary>
internal sealed class CategoryNodesBySlugSpec : Specification<Category, CategoryNode>
{
    public CategoryNodesBySlugSpec(string slug, Guid? ownerId)
    {
        var value = slug.Trim().ToLower();
        Where(c => c.Slug == value);
        if (ownerId is { } owner)
            Where(c => c.OwnerId == owner);
        IgnoreOwnership();
        Select(c => new CategoryNode(c.Id, c.ParentId, c.OwnerId));
    }
}

internal sealed class CategoryNodesByOwnersSpec : Specification<Category, CategoryNode>
{
    public CategoryNodesByOwnersSpec(IReadOnlyCollection<Guid> ownerIds)
    {
        Where(c => ownerIds.Contains(c.OwnerId));
        IgnoreOwnership();
        Select(c => new CategoryNode(c.Id, c.ParentId, c.OwnerId));
    }
}

internal sealed class TagIdsBySlugSpec : Specification<Tag, Guid>
{
    public TagIdsBySlugSpec(string slug, Guid? ownerId)
    {
        var value = slug.Trim().ToLower();
        Where(t => t.Slug == value);
        if (ownerId is { } owner)
            Where(t => t.OwnerId == owner);
        IgnoreOwnership();
        Select(t => t.Id);
    }
}

internal sealed class PublishedPostIdSpec : Specification<Post, Guid>
{
    public PublishedPostIdSpec(Guid authorId, string slug)
    {
        var value = slug.Trim().ToLower();
        Where(p => p.OwnerId == authorId && p.Slug == value && p.Status == PostStatus.Published);
        IgnoreOwnership();
        Select(p => p.Id);
    }
}

internal sealed class PublishedPostByIdSpec : Specification<Post>
{
    public PublishedPostByIdSpec(Guid id)
    {
        Where(p => p.Id == id && p.Status == PostStatus.Published);
        IgnoreOwnership();
        ReadOnly();
    }
}

/// <summary>Muallifning oldingi (eskiroq) yoki keyingi (yangiroq) nashr qilingan posti.</summary>
internal sealed class AdjacentPostSpec : Specification<Post, PostNavLinkDto>
{
    public AdjacentPostSpec(Guid authorId, Guid currentId, DateTimeOffset publishedAt, bool older)
    {
        Where(p => p.OwnerId == authorId && p.Status == PostStatus.Published && p.Id != currentId);
        if (older)
        {
            Where(p => p.PublishedAt < publishedAt);
            OrderByDesc(p => p.PublishedAt!);
        }
        else
        {
            Where(p => p.PublishedAt > publishedAt);
            OrderByAsc(p => p.PublishedAt!);
        }

        IgnoreOwnership();
        Select(p => new PostNavLinkDto(p.Title, p.Slug));
    }
}

internal sealed class UserReactionSpec : Specification<Reaction, ReactionType>
{
    public UserReactionSpec(Guid userId, Guid postId)
    {
        Where(r => r.UserId == userId && r.TargetType == ReactionTargetType.Post && r.TargetId == postId);
        Select(r => r.Type);
    }
}

/// <summary>Vaqti kelgan rejalashtirilgan postlar (background job: foydalanuvchi yo'q → ownership chetlab o'tiladi).</summary>
internal sealed class DueScheduledPostsSpec : Specification<Post>
{
    public DueScheduledPostsSpec(DateTimeOffset now, int take)
    {
        Where(p => p.Status == PostStatus.Scheduled && p.ScheduledAt != null && p.ScheduledAt <= now);
        OrderByAsc(p => p.ScheduledAt!);
        IgnoreOwnership();
        Paginate(1, take);
    }
}
