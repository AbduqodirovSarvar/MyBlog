using System.Text.Json;
using System.Text.Json.Serialization;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;

namespace MyBlog.Application.Features.Posts;

// ---------- Umumiy ----------

public sealed record PostCountsDto(long Views, int Likes, int Dislikes, int Comments);

public sealed record PostSeoDto(string? MetaTitle, string? MetaDescription, Guid? OgImageMediaId, string? CanonicalUrl);

/// <param name="Raw">Muharrir hujjati (JSON qiymat sifatida, masalan TipTap doc obyekti); "html" formatida null.</param>
public sealed record PostContentDto(string Format, string Html, JsonElement? Raw);

// ---------- Boshqaruv (api/my/posts) ----------

public sealed record PostTagDto(Guid Id, string Name, string Slug);

/// <summary>Post saqlanganidan keyin yozilgan autosave (muharrir "saqlanmagan o'zgarishlarni tiklash"ni taklif qiladi).</summary>
public sealed record PostAutosaveInfoDto(Guid RevisionId, string Title, DateTimeOffset SavedAt);

public sealed record PostDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PostStatus>))] PostStatus Status,
    Guid? CategoryId,
    IReadOnlyList<PostTagDto> Tags,
    Guid? CoverMediaId,
    string? CoverUrl,
    PostContentDto Content,
    IReadOnlyList<TocItem> Toc,
    int ReadingTimeMinutes,
    bool AllowComments,
    bool IsFeatured,
    PostSeoDto Seo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledAt,
    PostCountsDto Counts,
    PostAutosaveInfoDto? Autosave,
    uint Version);

public sealed record MyPostListItemDto(
    Guid Id,
    string Title,
    string Slug,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PostStatus>))] PostStatus Status,
    Guid? CategoryId,
    string? CoverUrl,
    bool IsFeatured,
    int ReadingTimeMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledAt,
    PostCountsDto Counts);

public sealed record PostRevisionListItemDto(
    Guid Id,
    int Number,
    [property: JsonConverter(typeof(JsonStringEnumConverter<RevisionKind>))] RevisionKind Kind,
    string Title,
    DateTimeOffset CreatedAt);

public sealed record PostRevisionDto(
    Guid Id,
    Guid PostId,
    int Number,
    [property: JsonConverter(typeof(JsonStringEnumConverter<RevisionKind>))] RevisionKind Kind,
    string Title,
    PostContentDto Content,
    DateTimeOffset CreatedAt);

public sealed record AutosaveResultDto(Guid RevisionId, int Number, DateTimeOffset SavedAt);

// ---------- Public (api/public/...) ----------

public sealed record PublicAuthorDto(string Username, string DisplayName, string? AvatarUrl);

public sealed record PublicCategoryDto(string Slug, string Name);

public sealed record PublicTagDto(string Name, string Slug);

public sealed record PublicPostSummaryDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? CoverUrl,
    DateTimeOffset? PublishedAt,
    int ReadingTimeMinutes,
    bool IsFeatured,
    PublicAuthorDto Author,
    PublicCategoryDto? Category,
    IReadOnlyList<PublicTagDto> Tags,
    PostCountsDto Counts);

public sealed record PublicPostSeoDto(string Title, string? Description, string? CanonicalUrl, string? OgImageUrl);

public sealed record PostNavLinkDto(string Title, string Slug);

public sealed record PublicPostDetailDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string Html,
    IReadOnlyList<TocItem> Toc,
    string? CoverUrl,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    int ReadingTimeMinutes,
    bool AllowComments,
    bool IsFeatured,
    PublicPostSeoDto Seo,
    PublicAuthorDto Author,
    PublicCategoryDto? Category,
    IReadOnlyList<PublicTagDto> Tags,
    PostCountsDto Counts,
    PostNavLinkDto? Previous,
    PostNavLinkDto? Next,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReactionType>))] ReactionType? MyReaction);
