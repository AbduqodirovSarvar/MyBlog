using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.CreateComment;
using MyBlog.Application.Features.Comments.DeleteComment;
using MyBlog.Application.Features.Comments.EditComment;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;
using MyBlog.Domain.Posts;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Comments;

public sealed class CommentCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostOwner = Guid.CreateVersion7();
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private readonly ICommentRepository _comments = Substitute.For<ICommentRepository>();
    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CommentsOptions _options = new();
    private readonly CommentPostInfo _post;

    public CommentCommandTests()
    {
        _post = Post();
        _comments.GetPostInfoAsync(_post.Id, Arg.Any<CancellationToken>()).Returns(_post);
        _comments.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CommentUserInfo>
            {
                [Alice] = new(Alice, "alice", "Alice", null, "uz", true, true)
            });
        _reactions.GetUserReactionsAsync(default, default, default!, default)
            .ReturnsForAnyArgs(new Dictionary<Guid, Domain.Reactions.ReactionType>());
    }

    private static CommentPostInfo Post(PostStatus status = PostStatus.Published, bool allowComments = true, bool deleted = false) =>
        new(Guid.CreateVersion7(), PostOwner, "owner", "Title", "title", status, allowComments, deleted, 0);

    private CreateCommentCommandHandler CreateHandler(Guid? user) =>
        new(_comments, _reactions, _unitOfWork, new FakeCurrentUser(user, Permissions.Comments.Write),
            Substitute.For<IFileStorage>(), Options.Create(_options), new FixedTimeProvider(Now));

    private EditCommentCommandHandler EditHandler(Guid user) =>
        new(_comments, _reactions, _unitOfWork, new FakeCurrentUser(user, Permissions.Comments.Write),
            Substitute.For<IFileStorage>(), Options.Create(_options), new FixedTimeProvider(Now));

    private DeleteCommentCommandHandler DeleteHandler(Guid user, params string[] permissions) =>
        new(_comments, _unitOfWork, new FakeCurrentUser(user, permissions));

    private Comment ExistingComment(Guid author, Comment? parent = null)
    {
        var comment = Comment.Create(_post.Id, author, "Existing", parent).Value.WithCreatedAt(Now.AddMinutes(-30));
        comment.ClearDomainEvents();
        _comments.GetByIdAsync(comment.Id, Arg.Any<CancellationToken>()).Returns(comment);
        _comments.GetIncludingDeletedAsync(comment.Id, Arg.Any<CancellationToken>()).Returns(comment);
        return comment;
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_on_post_with_comments_disabled_is_rejected()
    {
        var post = Post(allowComments: false);
        _comments.GetPostInfoAsync(post.Id, Arg.Any<CancellationToken>()).Returns(post);

        var result = await CreateHandler(Alice).Handle(new CreateCommentCommand(post.Id, "Salom"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.CommentsDisabled);
        _comments.DidNotReceive().Add(Arg.Any<Comment>());
        _unitOfWork.SaveChangesCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(PostStatus.Draft, false)]
    [InlineData(PostStatus.Scheduled, false)]
    [InlineData(PostStatus.Archived, false)]
    [InlineData(PostStatus.Published, true)]
    public async Task Create_on_unpublished_or_deleted_post_is_rejected(PostStatus status, bool deleted)
    {
        var post = Post(status, deleted: deleted);
        _comments.GetPostInfoAsync(post.Id, Arg.Any<CancellationToken>()).Returns(post);

        var result = await CreateHandler(Alice).Handle(new CreateCommentCommand(post.Id, "Salom"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.PostNotFound);
        _comments.DidNotReceive().Add(Arg.Any<Comment>());
    }

    [Fact]
    public async Task Create_reply_deeper_than_max_depth_is_rejected()
    {
        var root = ExistingComment(Bob);
        var reply = ExistingComment(Bob, root);
        var replyToReply = ExistingComment(Bob, reply); // Depth 2 — oxirgi daraja

        var result = await CreateHandler(Alice).Handle(
            new CreateCommentCommand(_post.Id, "Too deep", replyToReply.Id), TestContext.Current.CancellationToken);

        replyToReply.Depth.ShouldBe(2);
        result.Error.Code.ShouldBe(CommentErrors.MaxDepthExceeded.Code);
        _comments.DidNotReceive().Add(Arg.Any<Comment>());
    }

    [Fact]
    public async Task Create_reply_respects_configured_max_depth()
    {
        _options.MaxDepth = 2;
        var root = ExistingComment(Bob);
        var reply = ExistingComment(Bob, root);

        var result = await CreateHandler(Alice).Handle(
            new CreateCommentCommand(_post.Id, "Depth 2", reply.Id), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(CommentErrors.MaxDepthExceeded.Code);
        result.Error.Args.ShouldBe([2]);
    }

    [Fact]
    public async Task Create_reply_to_unknown_parent_is_rejected()
    {
        var result = await CreateHandler(Alice).Handle(
            new CreateCommentCommand(_post.Id, "Hi", Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.ParentNotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("\u0001\u0002")]
    public async Task Create_with_empty_content_is_rejected(string content)
    {
        var result = await CreateHandler(Alice).Handle(new CreateCommentCommand(_post.Id, content), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.ContentRequired);
    }

    [Fact]
    public async Task Create_with_too_long_content_uses_configured_limit()
    {
        _options.MaxLength = 10;

        var result = await CreateHandler(Alice).Handle(new CreateCommentCommand(_post.Id, new string('a', 11)),
            TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(CommentErrors.ContentTooLong.Code);
        result.Error.Args.ShouldBe([10]);
    }

    [Fact]
    public async Task Create_adds_comment_increments_post_counter_and_returns_dto()
    {
        var result = await CreateHandler(Alice).Handle(
            new CreateCommentCommand(_post.Id, "  Zo'r\u0007 maqola!  "), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Content.ShouldBe("Zo'r maqola!");
        dto.Depth.ShouldBe(0);
        dto.Author.ShouldNotBeNull().Username.ShouldBe("alice");
        dto.CanEdit.ShouldBeTrue();
        dto.CanDelete.ShouldBeTrue();
        dto.CanReply.ShouldBeTrue();
        dto.Deleted.ShouldBeFalse();

        _comments.Received(1).Add(Arg.Is<Comment>(c => c.Id == dto.Id && c.AuthorId == Alice));
        await _comments.Received(1).AdjustPostCommentCountAsync(_post.Id, 1, Arg.Any<CancellationToken>());
        _unitOfWork.TransactionCalls.ShouldBe(1);
        _unitOfWork.SaveChangesCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Create_reply_sets_depth_and_parent()
    {
        var root = ExistingComment(Bob);

        var result = await CreateHandler(Alice).Handle(new CreateCommentCommand(_post.Id, "Javob", root.Id),
            TestContext.Current.CancellationToken);

        result.Value.ParentId.ShouldBe(root.Id);
        result.Value.Depth.ShouldBe(1);
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_by_non_author_is_forbidden()
    {
        var comment = ExistingComment(Bob);

        var result = await EditHandler(Alice).Handle(new EditCommentCommand(comment.Id, "Hacked"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.NotAuthor);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        comment.Content.ShouldBe("Existing");
        _unitOfWork.SaveChangesCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Edit_by_author_marks_comment_edited()
    {
        var comment = ExistingComment(Alice);

        var result = await EditHandler(Alice).Handle(new EditCommentCommand(comment.Id, "Yangilangan"), TestContext.Current.CancellationToken);

        result.Value.Content.ShouldBe("Yangilangan");
        result.Value.IsEdited.ShouldBeTrue();
        result.Value.EditedAt.ShouldBe(Now);
        _unitOfWork.SaveChangesCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Edit_after_edit_window_is_rejected()
    {
        _options.EditWindowMinutes = 15;
        var comment = ExistingComment(Alice); // 30 daqiqa oldin yozilgan

        var result = await EditHandler(Alice).Handle(new EditCommentCommand(comment.Id, "Kech"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(CommentErrors.EditWindowExpired.Code);
        result.Error.Args.ShouldBe([15]);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_by_post_owner_is_allowed_and_decrements_counter()
    {
        var comment = ExistingComment(Alice);

        var result = await DeleteHandler(PostOwner).Handle(new DeleteCommentCommand(comment.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _comments.Received(1).Remove(comment);
        await _comments.Received(1).AdjustPostCommentCountAsync(_post.Id, -1, Arg.Any<CancellationToken>());
        _unitOfWork.SaveChangesCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_by_author_and_by_moderator_is_allowed()
    {
        var own = ExistingComment(Alice);
        var other = ExistingComment(Bob);

        (await DeleteHandler(Alice).Handle(new DeleteCommentCommand(own.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        (await DeleteHandler(Guid.CreateVersion7(), Permissions.Comments.Moderate)
            .Handle(new DeleteCommentCommand(other.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_by_other_user_is_forbidden()
    {
        var comment = ExistingComment(Bob);

        var result = await DeleteHandler(Alice, Permissions.Comments.Write)
            .Handle(new DeleteCommentCommand(comment.Id), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.DeleteForbidden);
        _comments.DidNotReceive().Remove(Arg.Any<Comment>());
        await _comments.DidNotReceive().AdjustPostCommentCountAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
