// NSubstitute *ForAnyArgs/*WithAnyArgs chaqiruvlarida CancellationToken argument faqat moslash uchun.
#pragma warning disable xUnit1051

using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.GetPostComments;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Comments;

public sealed class CommentQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private readonly ICommentRepository _comments = Substitute.For<ICommentRepository>();
    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();
    private readonly CommentPostInfo _post = new(Guid.CreateVersion7(), Owner, "owner", "T", "t", PostStatus.Published, true, false, 3);

    public CommentQueryTests()
    {
        _comments.GetPostInfoAsync(_post.Id, Arg.Any<CancellationToken>()).Returns(_post);
        _comments.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CommentUserInfo>
            {
                [Alice] = new(Alice, "alice", "Alice", "avatars/a.webp", "uz", true, true),
                [Bob] = new(Bob, "bob", "Bob", null, "uz", true, true)
            });
    }

    private Comment New(Guid author, Comment? parent, int minutes)
    {
        var comment = Comment.Create(_post.Id, author, $"c{minutes}", parent).Value.WithCreatedAt(Now.AddMinutes(minutes));
        comment.ClearDomainEvents();
        return comment;
    }

    private GetPostCommentsQueryHandler Handler(ICurrentUser user)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>());
        return new(_comments, _reactions, user, storage, Options.Create(new CommentsOptions()), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Builds_tree_with_placeholders_my_reactions_and_permissions()
    {
        var root = New(Alice, null, 0);
        var deletedWithReplies = New(Bob, root, 1);
        var grandChild = New(Alice, deletedWithReplies, 2);
        var deletedLeaf = New(Bob, root, 3);
        var secondReply = New(Bob, root, 4);
        deletedWithReplies.MarkDeleted();
        deletedLeaf.MarkDeleted();

        _comments.GetRootPageAsync(_post.Id, CommentSort.Oldest, 1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedList<Comment>([root], 1, 20, 1));
        // Ataylab teskari tartibda: handler eskisini birinchi qo'yishi kerak.
        _comments.GetRepliesAsync(_post.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([secondReply, deletedLeaf, grandChild, deletedWithReplies]);
        _reactions.GetUserReactionsAsync(Bob, ReactionTargetType.Comment, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ReactionType> { [root.Id] = ReactionType.Dislike });

        var result = await Handler(new FakeCurrentUser(Bob, Permissions.Comments.Write))
            .Handle(new GetPostCommentsQuery(_post.Id), TestContext.Current.CancellationToken);

        var page = result.Value;
        page.TotalCount.ShouldBe(1);
        var dto = page.Items.ShouldHaveSingleItem();
        dto.Id.ShouldBe(root.Id);
        dto.Author!.AvatarUrl.ShouldBe("/media/avatars/a.webp");
        dto.MyReaction.ShouldBe("Dislike");
        dto.CanEdit.ShouldBeFalse();
        dto.CanDelete.ShouldBeFalse();
        dto.CanReply.ShouldBeTrue();

        dto.Replies.Select(r => r.Id).ShouldBe([deletedWithReplies.Id, secondReply.Id]);

        var placeholder = dto.Replies[0];
        placeholder.Deleted.ShouldBeTrue();
        placeholder.Content.ShouldBeNull();
        placeholder.Author.ShouldBeNull();
        placeholder.Replies.ShouldHaveSingleItem().Id.ShouldBe(grandChild.Id);
        placeholder.Replies[0].CanReply.ShouldBeFalse(); // Depth 2 — oxirgi daraja

        var bobsReply = dto.Replies[1];
        bobsReply.CanEdit.ShouldBeTrue();
        bobsReply.CanDelete.ShouldBeTrue();
        bobsReply.MyReaction.ShouldBeNull();
    }

    [Fact]
    public async Task Post_owner_can_delete_any_comment_and_anonymous_cannot_do_anything()
    {
        var root = New(Alice, null, 0);
        _comments.GetRootPageAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(new PagedList<Comment>([root], 1, 20, 1));
        _comments.GetRepliesAsync(default, default!, default).ReturnsForAnyArgs(new List<Comment>());

        var asOwner = await Handler(new FakeCurrentUser(Owner, Permissions.Comments.Write))
            .Handle(new GetPostCommentsQuery(_post.Id), TestContext.Current.CancellationToken);
        var asAnonymous = await Handler(new FakeCurrentUser(null))
            .Handle(new GetPostCommentsQuery(_post.Id), TestContext.Current.CancellationToken);

        asOwner.Value.Items[0].CanDelete.ShouldBeTrue();
        asOwner.Value.Items[0].CanEdit.ShouldBeFalse();
        asAnonymous.Value.Items[0].ShouldSatisfyAllConditions(
            c => c.CanDelete.ShouldBeFalse(),
            c => c.CanEdit.ShouldBeFalse(),
            c => c.CanReply.ShouldBeFalse(),
            c => c.MyReaction.ShouldBeNull());
    }

    [Fact]
    public async Task Draft_post_comments_are_hidden_from_others()
    {
        var draft = _post with { Status = PostStatus.Draft };
        _comments.GetPostInfoAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await Handler(new FakeCurrentUser(Alice)).Handle(new GetPostCommentsQuery(draft.Id), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommentErrors.PostNotFound);
    }
}

public sealed class CommentContentTests
{
    [Theory]
    [InlineData("  salom  ", "salom")]
    [InlineData("a\r\nb\rc", "a\nb\nc")]
    [InlineData("a\n\n\n\n\nb", "a\n\nb")]
    [InlineData("a\u0000b\u0007c\u001Bd", "abcd")]
    [InlineData("tab\there", "tab\there")]
    [InlineData("rtl‮trick", "rtltrick")]
    [InlineData("<b>html</b>", "<b>html</b>")]
    public void Normalizes_plain_text(string input, string expected) =>
        CommentContent.Normalize(input).ShouldBe(expected);
}
