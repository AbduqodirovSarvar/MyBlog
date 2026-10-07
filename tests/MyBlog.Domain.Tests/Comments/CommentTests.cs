using MyBlog.Domain.Comments;
using MyBlog.Domain.Comments.Events;

namespace MyBlog.Domain.Tests.Comments;

public sealed class CommentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostId = Guid.CreateVersion7();

    private static Comment Root(Guid? authorId = null) =>
        Comment.Create(PostId, authorId ?? Guid.CreateVersion7(), "Root comment").Value;

    [Fact]
    public void Create_Root_HasDepthZeroAndRaisesEvent()
    {
        var authorId = Guid.CreateVersion7();

        var comment = Comment.Create(PostId, authorId, "  Salom  ").Value;

        comment.Depth.ShouldBe(0);
        comment.ParentId.ShouldBeNull();
        comment.Content.ShouldBe("Salom");
        var domainEvent = comment.DomainEvents.OfType<CommentCreatedDomainEvent>().ShouldHaveSingleItem();
        domainEvent.ShouldBe(new CommentCreatedDomainEvent(comment.Id, PostId, authorId, null, null));
    }

    [Fact]
    public void Create_Reply_IncrementsDepthAndReferencesParentAuthor()
    {
        var parentAuthor = Guid.CreateVersion7();
        var parent = Root(parentAuthor);

        var reply = Comment.Create(PostId, Guid.CreateVersion7(), "Reply", parent).Value;

        reply.Depth.ShouldBe(1);
        reply.ParentId.ShouldBe(parent.Id);
        reply.DomainEvents.OfType<CommentCreatedDomainEvent>().ShouldHaveSingleItem().ParentAuthorId.ShouldBe(parentAuthor);
    }

    [Fact]
    public void Create_BeyondMaxDepth_Fails()
    {
        var level0 = Root();
        var level1 = Comment.Create(PostId, Guid.CreateVersion7(), "1", level0).Value;
        var level2 = Comment.Create(PostId, Guid.CreateVersion7(), "2", level1).Value;

        level2.Depth.ShouldBe(CommentConstraints.MaxDepth - 1);
        Comment.Create(PostId, Guid.CreateVersion7(), "3", level2).Error.ShouldBe(CommentErrors.MaxDepthExceeded);
    }

    [Fact]
    public void Create_ParentFromAnotherPost_Fails()
    {
        var parent = Root();

        Comment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "x", parent)
            .Error.ShouldBe(CommentErrors.ParentFromAnotherPost);
    }

    [Fact]
    public void Create_ReplyToDeleted_Fails()
    {
        var parent = Root();
        parent.MarkDeleted();

        Comment.Create(PostId, Guid.CreateVersion7(), "x", parent).Error.ShouldBe(CommentErrors.ParentDeleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyContent_Fails(string content) =>
        Comment.Create(PostId, Guid.CreateVersion7(), content).Error.ShouldBe(CommentErrors.ContentRequired);

    [Fact]
    public void Create_TooLongContent_Fails() =>
        Comment.Create(PostId, Guid.CreateVersion7(), new string('x', CommentConstraints.ContentMaxLength + 1))
            .Error.ShouldBe(CommentErrors.ContentTooLong);

    [Fact]
    public void Edit_ChangesContentAndMarksEdited()
    {
        var comment = Root();

        comment.Edit("Updated", Now).IsSuccess.ShouldBeTrue();

        comment.Content.ShouldBe("Updated");
        comment.IsEdited.ShouldBeTrue();
        comment.EditedAt.ShouldBe(Now);
    }

    [Fact]
    public void Edit_SameContent_DoesNotMarkEdited()
    {
        var comment = Root();

        comment.Edit("Root comment", Now).IsSuccess.ShouldBeTrue();

        comment.IsEdited.ShouldBeFalse();
        comment.EditedAt.ShouldBeNull();
    }

    [Fact]
    public void Edit_Deleted_Fails()
    {
        var comment = Root();
        comment.MarkDeleted();

        comment.Edit("x", Now).Error.ShouldBe(CommentErrors.Deleted);
    }

    [Fact]
    public void MarkDeleted_RaisesEventOnce()
    {
        var comment = Root();

        comment.MarkDeleted();
        comment.MarkDeleted();

        comment.DomainEvents.OfType<CommentDeletedDomainEvent>().ShouldHaveSingleItem();
    }
}
