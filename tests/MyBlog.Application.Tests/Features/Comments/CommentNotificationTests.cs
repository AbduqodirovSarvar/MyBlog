// NSubstitute *ForAnyArgs/*WithAnyArgs chaqiruvlarida CancellationToken argument faqat moslash uchun.
#pragma warning disable xUnit1051

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Notifications;
using MyBlog.Domain.Comments.Events;
using MyBlog.Domain.Posts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyBlog.Application.Tests.Features.Comments;

public sealed class CommentNotificationTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private static readonly Guid PostId = Guid.CreateVersion7();
    private static readonly Guid CommentId = Guid.CreateVersion7();

    private readonly ICommentRepository _comments = Substitute.For<ICommentRepository>();
    private readonly IUserContactLookup _contacts = Substitute.For<IUserContactLookup>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailQueue _queue = Substitute.For<IEmailQueue>();
    private readonly Dictionary<Guid, CommentUserInfo> _users = new()
    {
        [Owner] = new(Owner, "owner", "Owner", null, "ru", NotifyOnComment: true, NotifyOnReply: true),
        [Alice] = new(Alice, "alice", "Alice", null, "en", NotifyOnComment: true, NotifyOnReply: true),
        [Bob] = new(Bob, "bob", "Bob", null, "uz-Cyrl", NotifyOnComment: true, NotifyOnReply: true)
    };

    public CommentNotificationTests()
    {
        _comments.GetPostInfoAsync(PostId, Arg.Any<CancellationToken>())
            .Returns(new CommentPostInfo(PostId, Owner, "owner", "Post title", "post-title", PostStatus.Published, true, false, 1));
        _comments.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => _users);
        _contacts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => $"{_users[ci.Arg<Guid>()].Username}@example.com");
        _renderer.RenderAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => new RenderedEmail($"subject:{ci.ArgAt<string>(0)}:{ci.ArgAt<string>(1)}", "<p>html</p>", "text"));
    }

    private CommentCreatedNotificationHandler Handler() =>
        new(_comments, _contacts, _renderer, _queue,
            Options.Create(new CommentLinkOptions { BaseUrl = "https://blog.example/" }),
            NullLogger<CommentCreatedNotificationHandler>.Instance);

    private static CommentCreatedDomainEvent TopLevel(Guid author) => new(CommentId, PostId, author, null, null);

    private static CommentCreatedDomainEvent Reply(Guid author, Guid parentAuthor) =>
        new(CommentId, PostId, author, Guid.CreateVersion7(), parentAuthor);

    [Fact]
    public async Task Top_level_comment_notifies_post_author_in_recipient_culture()
    {
        await Handler().Handle(TopLevel(Alice), TestContext.Current.CancellationToken);

        await _renderer.Received(1).RenderAsync(
            CommentCreatedNotificationHandler.CommentOnPostTemplate,
            "ru",
            Arg.Is<IReadOnlyDictionary<string, string>>(v =>
                v["link"] == $"https://blog.example/ru/owner/post-title#comment-{CommentId}"
                && v["commenterName"] == "Alice"
                && v["recipientName"] == "Owner"
                && v["postTitle"] == "Post title"),
            Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(
            Arg.Is<EmailMessage>(m => m.To == "owner@example.com" && m.Subject == "subject:comment-on-post:ru"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Own_comment_on_own_post_is_not_notified()
    {
        await Handler().Handle(TopLevel(Owner), TestContext.Current.CancellationToken);

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Reply_to_own_comment_by_post_owner_is_not_notified()
    {
        await Handler().Handle(Reply(Owner, Owner), TestContext.Current.CancellationToken);

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Post_author_opted_out_is_not_notified()
    {
        _users[Owner] = _users[Owner] with { NotifyOnComment = false };

        await Handler().Handle(TopLevel(Alice), TestContext.Current.CancellationToken);

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Reply_notifies_parent_author_and_post_author_with_own_templates_and_cultures()
    {
        await Handler().Handle(Reply(Alice, Bob), TestContext.Current.CancellationToken);

        await _queue.Received(1).EnqueueAsync(
            Arg.Is<EmailMessage>(m => m.To == "bob@example.com" && m.Subject == "subject:comment-reply:uz-Cyrl"),
            Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(
            Arg.Is<EmailMessage>(m => m.To == "owner@example.com" && m.Subject == "subject:comment-on-post:ru"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_respects_parent_author_opt_out_but_still_notifies_post_author()
    {
        _users[Bob] = _users[Bob] with { NotifyOnReply = false };

        await Handler().Handle(Reply(Alice, Bob), TestContext.Current.CancellationToken);

        await _queue.Received(1).EnqueueAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Is<EmailMessage>(m => m.To == "owner@example.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_to_post_author_comment_sends_single_reply_email()
    {
        await Handler().Handle(Reply(Alice, Owner), TestContext.Current.CancellationToken);

        await _queue.Received(1).EnqueueAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(
            Arg.Is<EmailMessage>(m => m.To == "owner@example.com" && m.Subject == "subject:comment-reply:ru"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recipient_without_confirmed_email_is_skipped()
    {
        _contacts.GetEmailAsync(Owner, Arg.Any<CancellationToken>()).Returns((string?)null);

        await Handler().Handle(TopLevel(Alice), TestContext.Current.CancellationToken);

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Failures_are_swallowed()
    {
        _renderer.RenderAsync(default!, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("template missing"));

        await Should.NotThrowAsync(() => Handler().Handle(TopLevel(Alice), TestContext.Current.CancellationToken));
    }
}
