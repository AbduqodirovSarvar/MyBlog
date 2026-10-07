using MyBlog.Domain.Common;

namespace MyBlog.Domain.Reactions;

public enum ReactionTargetType
{
    Post = 1,
    Comment = 2
}

public enum ReactionType
{
    Like = 1,
    Dislike = 2
}

/// <summary>
/// Foydalanuvchining post yoki izohga reaksiyasi. (UserId, TargetType, TargetId) unikal.
/// Target polimorf, shuning uchun FK yo'q; hisoblagichlar repository'da atomar yangilanadi.
/// </summary>
public sealed class Reaction : Entity, IAggregateRoot
{
    private Reaction() { }

    private Reaction(Guid userId, ReactionTargetType targetType, Guid targetId, ReactionType type, DateTimeOffset createdAt)
    {
        UserId = userId;
        TargetType = targetType;
        TargetId = targetId;
        Type = type;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }
    public ReactionTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public ReactionType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Result<Reaction> Create(Guid userId, ReactionTargetType targetType, Guid targetId, ReactionType type,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            return ReactionErrors.InvalidUser;
        if (targetId == Guid.Empty || !Enum.IsDefined(targetType))
            return ReactionErrors.InvalidTarget;
        if (!Enum.IsDefined(type))
            return ReactionErrors.InvalidType;

        return new Reaction(userId, targetType, targetId, type, now.ToUniversalTime());
    }

    /// <summary>Turini o'zgartiradi. Tur bir xil bo'lsa hech narsa qilmaydi.</summary>
    public Result ChangeType(ReactionType type, DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
            return ReactionErrors.InvalidType;
        if (type == Type)
            return Result.Success();

        Type = type;
        UpdatedAt = now.ToUniversalTime();
        return Result.Success();
    }
}

public static class ReactionErrors
{
    public static readonly Error NotFound = Error.NotFound("Reaction.NotFound", "Reaction was not found.");
    public static readonly Error InvalidUser = Error.Validation("Reaction.InvalidUser", "User is required.");
    public static readonly Error InvalidTarget = Error.Validation("Reaction.InvalidTarget", "Reaction target is not valid.");
    public static readonly Error InvalidType = Error.Validation("Reaction.InvalidType", "Reaction type is not valid.");
    public static readonly Error TargetNotFound = Error.NotFound("Reaction.TargetNotFound", "Reaction target was not found.");
}
