// NSubstitute *ForAnyArgs/*WithAnyArgs chaqiruvlarida CancellationToken argument faqat moslash uchun.
#pragma warning disable xUnit1051

using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Features.Reactions.Abstractions;
using MyBlog.Application.Features.Reactions.RemoveReaction;
using MyBlog.Application.Features.Reactions.SetReaction;
using MyBlog.Domain.Reactions;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Reactions;

public sealed class ReactionCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Target = Guid.CreateVersion7();

    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();

    public ReactionCommandTests()
    {
        _reactions.IsTargetAvailableAsync(Arg.Any<ReactionTargetType>(), Target, Arg.Any<CancellationToken>()).Returns(true);
        _reactions.GetCountsAsync(Arg.Any<ReactionTargetType>(), Target, Arg.Any<CancellationToken>()).Returns(new ReactionCounts(5, 2));
        _reactions.TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>()).Returns(true);
        _reactions.TryChangeTypeAsync(default, default, default, default, default).ReturnsForAnyArgs(true);
        _reactions.TryDeleteAsync(default, default, default).ReturnsForAnyArgs(true);
    }

    private SetReactionCommandHandler SetHandler() =>
        new(_reactions, _unitOfWork, new FakeCurrentUser(User, Permissions.Reactions.Write), new FixedTimeProvider(Now));

    private RemoveReactionCommandHandler RemoveHandler() =>
        new(_reactions, _unitOfWork, new FakeCurrentUser(User, Permissions.Reactions.Write));

    private static Reaction Existing(ReactionType type, ReactionTargetType targetType = ReactionTargetType.Post) =>
        Reaction.Create(User, targetType, Target, type, Now.AddDays(-1)).Value;

    private void FindReturns(params Reaction?[] sequence) =>
        _reactions.FindAsync(User, Arg.Any<ReactionTargetType>(), Target, Arg.Any<CancellationToken>())
            .Returns(sequence[0], sequence[1..]);

    [Fact]
    public async Task First_like_inserts_and_increments_like_counter()
    {
        FindReturns((Reaction?)null);

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, ReactionType.Like),
            TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new(5, 2, "Like"));
        await _reactions.Received(1).TryInsertAsync(
            Arg.Is<Reaction>(r => r.UserId == User && r.TargetId == Target && r.Type == ReactionType.Like), Arg.Any<CancellationToken>());
        await _reactions.Received(1).AdjustCountersAsync(ReactionTargetType.Post, Target, 1, 0, Arg.Any<CancellationToken>());
        _unitOfWork.TransactionCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Switching_like_to_dislike_adjusts_both_counters()
    {
        var existing = Existing(ReactionType.Like, ReactionTargetType.Comment);
        FindReturns(existing);

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Comment, Target, ReactionType.Dislike),
            TestContext.Current.CancellationToken);

        result.Value.MyReaction.ShouldBe("Dislike");
        await _reactions.Received(1).TryChangeTypeAsync(existing.Id, ReactionType.Like, ReactionType.Dislike, Now, Arg.Any<CancellationToken>());
        await _reactions.Received(1).AdjustCountersAsync(ReactionTargetType.Comment, Target, -1, 1, Arg.Any<CancellationToken>());
        await _reactions.DidNotReceive().TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_reaction_twice_is_idempotent()
    {
        FindReturns(Existing(ReactionType.Like));

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, ReactionType.Like),
            TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new(5, 2, "Like"));
        await _reactions.DidNotReceiveWithAnyArgs().TryInsertAsync(default!, default);
        await _reactions.DidNotReceiveWithAnyArgs().TryChangeTypeAsync(default, default, default, default, default);
        await _reactions.DidNotReceiveWithAnyArgs().AdjustCountersAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Concurrent_first_reaction_with_same_type_becomes_no_op()
    {
        // Birinchi o'qishda reaksiya yo'q, insert unique index'ga uriladi, qayta o'qishda parallel so'rov qo'ygani ko'rinadi.
        FindReturns(null, Existing(ReactionType.Like));
        _reactions.TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, ReactionType.Like),
            TestContext.Current.CancellationToken);

        result.Value.MyReaction.ShouldBe("Like");
        await _reactions.DidNotReceiveWithAnyArgs().AdjustCountersAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Concurrent_first_reaction_with_other_type_is_retried_as_switch()
    {
        var concurrent = Existing(ReactionType.Like);
        FindReturns(null, concurrent);
        _reactions.TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, ReactionType.Dislike),
            TestContext.Current.CancellationToken);

        result.Value.MyReaction.ShouldBe("Dislike");
        await _reactions.Received(1).AdjustCountersAsync(ReactionTargetType.Post, Target, -1, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Persistent_conflicts_return_conflict_error()
    {
        FindReturns((Reaction?)null);
        _reactions.TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, ReactionType.Like),
            TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ReactionErrors.ConcurrentUpdate);
        await _reactions.Received(SetReactionCommandHandler.MaxAttempts).TryInsertAsync(Arg.Any<Reaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reaction_on_unavailable_target_is_rejected()
    {
        var missing = Guid.CreateVersion7();

        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, missing, ReactionType.Like),
            TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ReactionErrors.TargetNotFound);
        _unitOfWork.TransactionCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_reaction_type_is_rejected()
    {
        var result = await SetHandler().Handle(new SetReactionCommand(ReactionTargetType.Post, Target, (ReactionType)42),
            TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ReactionErrors.InvalidType);
    }

    [Fact]
    public async Task Remove_deletes_reaction_and_decrements_its_counter()
    {
        var existing = Existing(ReactionType.Dislike);
        FindReturns(existing);

        var result = await RemoveHandler().Handle(new RemoveReactionCommand(ReactionTargetType.Post, Target),
            TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new(5, 2, null));
        await _reactions.Received(1).TryDeleteAsync(existing.Id, ReactionType.Dislike, Arg.Any<CancellationToken>());
        await _reactions.Received(1).AdjustCountersAsync(ReactionTargetType.Post, Target, 0, -1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_without_reaction_is_idempotent()
    {
        FindReturns((Reaction?)null);

        var result = await RemoveHandler().Handle(new RemoveReactionCommand(ReactionTargetType.Comment, Target),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _reactions.DidNotReceiveWithAnyArgs().TryDeleteAsync(default, default, default);
        await _reactions.DidNotReceiveWithAnyArgs().AdjustCountersAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Concurrent_remove_does_not_decrement_twice()
    {
        var existing = Existing(ReactionType.Like);
        FindReturns(existing, null);
        _reactions.TryDeleteAsync(default, default, default).ReturnsForAnyArgs(false);

        var result = await RemoveHandler().Handle(new RemoveReactionCommand(ReactionTargetType.Post, Target),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _reactions.DidNotReceiveWithAnyArgs().AdjustCountersAsync(default, default, default, default, default);
    }
}
