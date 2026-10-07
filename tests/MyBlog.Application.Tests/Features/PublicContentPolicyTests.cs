// NSubstitute *ForAnyArgs/*WithAnyArgs chaqiruvlarida CancellationToken argument faqat moslash uchun.
#pragma warning disable xUnit1051

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Authors.GetAuthorProfile;
using MyBlog.Application.Features.Authors.GetAuthors;
using MyBlog.Application.Features.Authors.GetAuthorTaxonomy;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Public;
using MyBlog.Application.Tests.Fakes;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using MyBlog.Domain.Tags;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Features;

/// <summary>IPublicContentPolicy semantikasi (default metodlar) va ommaviy handler'lardagi himoya.</summary>
public sealed class PublicContentPolicyTests
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryReadRepository<UserProfile> _profiles = new(
        [UserProfile.Create(Alice, "alice").Value, UserProfile.Create(Bob, "bob").Value]);

    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IContentStatsRepository _stats = Substitute.For<IContentStatsRepository>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    public PublicContentPolicyTests()
    {
        _postRepository.ListPublishedAsync(default!, default, default, default)
            .ReturnsForAnyArgs(ci => PagedList<PublicPostRow>.Empty(ci.ArgAt<int>(1), ci.ArgAt<int>(2)));
        _stats.CountPublishedPostsByOwnerAsync(default!, default)
            .ReturnsForAnyArgs(new Dictionary<Guid, int>());
    }

    // ---------- Policy ----------

    [Fact]
    public void Open_policy_allows_everything()
    {
        IPublicContentPolicy policy = FakePublicContentPolicy.Open;

        policy.CanReadOthersContent.ShouldBeTrue();
        policy.CanRead(Bob).ShouldBeTrue();
    }

    [Fact]
    public void Closed_policy_allows_only_own_content_and_nothing_for_anonymous()
    {
        IPublicContentPolicy alice = FakePublicContentPolicy.ClosedFor(Alice);
        IPublicContentPolicy anonymous = FakePublicContentPolicy.ClosedFor(null);

        alice.CanReadOthersContent.ShouldBeFalse();
        alice.CanRead(Alice).ShouldBeTrue();
        alice.CanRead(Bob).ShouldBeFalse();
        anonymous.CanRead(Guid.Empty).ShouldBeFalse();
        anonymous.CanRead(Bob).ShouldBeFalse();
    }

    // ---------- Postlar ----------

    private ListPublicPostsQueryHandler ListHandler(IPublicContentPolicy policy) =>
        new(_postRepository, _profiles, new InMemoryReadRepository<Category>([]), new InMemoryReadRepository<Tag>([]),
            new InMemoryReadRepository<MediaFile>([]), Substitute.For<IFileStorage>(), Substitute.For<ILocalizer>(), policy);

    [Fact]
    public async Task Open_system_lists_all_authors_posts()
    {
        (await ListHandler(FakePublicContentPolicy.Open).Handle(new ListPublicPostsQuery(), Ct)).IsSuccess.ShouldBeTrue();

        await _postRepository.Received(1).ListPublishedAsync(Arg.Is<PublicPostFilter>(f => f.AuthorId == null),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Closed_system_list_is_scoped_to_current_user()
    {
        var own = await ListHandler(FakePublicContentPolicy.ClosedFor(Alice)).Handle(new ListPublicPostsQuery(), Ct);
        var foreign = await ListHandler(FakePublicContentPolicy.ClosedFor(Alice)).Handle(new ListPublicPostsQuery(Author: "bob"), Ct);
        var anonymous = await ListHandler(FakePublicContentPolicy.ClosedFor(null)).Handle(new ListPublicPostsQuery(), Ct);

        own.IsSuccess.ShouldBeTrue();
        foreign.Value.Items.ShouldBeEmpty();
        anonymous.Value.Items.ShouldBeEmpty();
        await _postRepository.Received(1).ListPublishedAsync(Arg.Is<PublicPostFilter>(f => f.AuthorId == Alice),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _postRepository.DidNotReceive().ListPublishedAsync(Arg.Is<PublicPostFilter>(f => f.AuthorId != Alice),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Closed_system_foreign_post_detail_is_not_found()
    {
        var posts = Substitute.For<IReadRepository<Post>>();
        var handler = new GetPublicPostQueryHandler(posts, _postRepository, _profiles, new InMemoryReadRepository<Category>([]),
            new InMemoryReadRepository<Tag>([]), new InMemoryReadRepository<MediaFile>([]), new InMemoryReadRepository<Reaction>([]),
            Substitute.For<IFileStorage>(), Substitute.For<ILocalizer>(), _cache, new FakeCurrentUser(Alice),
            Options.Create(new PostsOptions()), FakePublicContentPolicy.ClosedFor(Alice),
            NullLogger<GetPublicPostQueryHandler>.Instance);

        var result = await handler.Handle(new GetPublicPostQuery("bob", "any-slug"), Ct);

        result.Error.ShouldBe(PostErrors.NotFound);
        await posts.DidNotReceiveWithAnyArgs().FirstOrDefaultAsync(Arg.Any<ISpecification<Post, Guid>>(), default);
        await _postRepository.DidNotReceiveWithAnyArgs().IncrementViewCountAsync(default, default);
    }

    // ---------- Mualliflar ----------

    [Fact]
    public async Task Closed_system_author_list_contains_only_current_user()
    {
        var aliceView = await new GetAuthorsQueryHandler(_profiles, _stats, new FakeMediaUrlResolver(),
            FakePublicContentPolicy.ClosedFor(Alice)).Handle(new GetAuthorsQuery(), Ct);
        var anonymousView = await new GetAuthorsQueryHandler(_profiles, _stats, new FakeMediaUrlResolver(),
            FakePublicContentPolicy.ClosedFor(null)).Handle(new GetAuthorsQuery(), Ct);
        var openView = await new GetAuthorsQueryHandler(_profiles, _stats, new FakeMediaUrlResolver(),
            FakePublicContentPolicy.Open).Handle(new GetAuthorsQuery(), Ct);

        aliceView.Value.Items.ShouldHaveSingleItem().Username.ShouldBe("alice");
        anonymousView.Value.Items.ShouldBeEmpty();
        openView.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Closed_system_foreign_author_profile_and_taxonomy_are_not_found()
    {
        var policy = FakePublicContentPolicy.ClosedFor(Alice);

        var profile = await new GetAuthorProfileQueryHandler(_profiles, _stats, new FakeMediaUrlResolver(), _cache, policy)
            .Handle(new GetAuthorProfileQuery("bob"), Ct);
        var tags = await new GetAuthorTagsQueryHandler(_profiles, new InMemoryReadRepository<Tag>([]), _stats, _cache, policy)
            .Handle(new GetAuthorTagsQuery("bob"), Ct);
        var categories = await new GetAuthorCategoriesQueryHandler(_profiles, new InMemoryReadRepository<Category>([]), _stats,
                new FakeMediaUrlResolver(), Substitute.For<ILocalizer>(), _cache, policy)
            .Handle(new GetAuthorCategoriesQuery("bob"), Ct);

        profile.Error.ShouldBe(UserProfileErrors.NotFound);
        tags.Error.ShouldBe(UserProfileErrors.NotFound);
        categories.Error.ShouldBe(UserProfileErrors.NotFound);
        _cache.ReceivedCalls().ShouldBeEmpty();
    }
}
