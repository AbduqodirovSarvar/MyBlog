using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.Email;

namespace MyBlog.Infrastructure.Tests.Email;

public sealed class TransactionalEmailQueueTests
{
    private static readonly EmailMessage Message = new("user@example.com", "Subject", "<p>Body</p>");

    private readonly ChannelEmailQueue _inner = new();

    [Fact]
    public async Task Without_transaction_message_is_enqueued_immediately()
    {
        var queue = new TransactionalEmailQueue(_inner);

        await queue.EnqueueAsync(Message, TestContext.Current.CancellationToken);

        _inner.Reader.TryRead(out var sent).ShouldBeTrue();
        sent.ShouldBe(Message);
    }

    [Fact]
    public async Task Inside_transaction_message_is_sent_only_after_commit()
    {
        var queue = new TransactionalEmailQueue(_inner);
        var ct = TestContext.Current.CancellationToken;

        await queue.TransactionStartedAsync(null!, null!, null!, ct);
        await queue.EnqueueAsync(Message, ct);
        _inner.Reader.TryRead(out _).ShouldBeFalse();

        await queue.TransactionCommittedAsync(null!, null!, ct);

        _inner.Reader.TryRead(out var sent).ShouldBeTrue();
        sent.ShouldBe(Message);
    }

    [Fact]
    public async Task Rolled_back_transaction_discards_pending_messages()
    {
        var queue = new TransactionalEmailQueue(_inner);
        var ct = TestContext.Current.CancellationToken;

        await queue.TransactionStartedAsync(null!, null!, null!, ct);
        await queue.EnqueueAsync(Message, ct);
        await queue.TransactionRolledBackAsync(null!, null!, ct);

        _inner.Reader.TryRead(out _).ShouldBeFalse();

        // Keyingi xat tranzaksiyasiz — darhol ketadi.
        await queue.EnqueueAsync(Message, ct);
        _inner.Reader.TryRead(out _).ShouldBeTrue();
    }
}
