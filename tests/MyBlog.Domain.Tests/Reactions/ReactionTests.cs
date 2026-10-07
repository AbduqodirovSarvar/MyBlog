using MyBlog.Domain.Reactions;

namespace MyBlog.Domain.Tests.Reactions;

public sealed class ReactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Reaction NewLike() =>
        Reaction.Create(Guid.CreateVersion7(), ReactionTargetType.Post, Guid.CreateVersion7(), ReactionType.Like, Now).Value;

    [Fact]
    public void Create_SetsFields()
    {
        var reaction = NewLike();

        reaction.Type.ShouldBe(ReactionType.Like);
        reaction.CreatedAt.ShouldBe(Now);
        reaction.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void Create_InvalidType_Fails() =>
        Reaction.Create(Guid.CreateVersion7(), ReactionTargetType.Comment, Guid.CreateVersion7(), (ReactionType)99, Now)
            .Error.ShouldBe(ReactionErrors.InvalidType);

    [Fact]
    public void Create_EmptyTarget_Fails() =>
        Reaction.Create(Guid.CreateVersion7(), ReactionTargetType.Post, Guid.Empty, ReactionType.Like, Now)
            .Error.ShouldBe(ReactionErrors.InvalidTarget);

    [Fact]
    public void ChangeType_ToOther_UpdatesTimestamp()
    {
        var reaction = NewLike();
        var later = Now.AddMinutes(5);

        reaction.ChangeType(ReactionType.Dislike, later).IsSuccess.ShouldBeTrue();

        reaction.Type.ShouldBe(ReactionType.Dislike);
        reaction.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void ChangeType_Same_IsNoOp()
    {
        var reaction = NewLike();

        reaction.ChangeType(ReactionType.Like, Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        reaction.UpdatedAt.ShouldBeNull();
    }
}
