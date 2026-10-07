using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Application.Features.Posts.Jobs;
using MyBlog.Application.Features.Posts.Manage;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Application.Tests.Fakes;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;
using NSubstitute;

namespace MyBlog.Application.Tests.Posts;

internal sealed class FakePostRepository(InMemoryRepository<PostRevision> revisions) : IPostRepository
{
    public Dictionary<Guid, uint> Versions { get; } = [];
    public Dictionary<Guid, uint> ExpectedVersions { get; } = [];
    public List<Guid> DeletedRevisionIds { get; } = [];

    public uint GetVersion(Post post) => Versions.GetValueOrDefault(post.Id, 1u);

    public void SetExpectedVersion(Post post, uint version) => ExpectedVersions[post.Id] = version;

    public Task IncrementViewCountAsync(Guid postId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteRevisionsAsync(IReadOnlyCollection<Guid> revisionIds, CancellationToken cancellationToken = default)
    {
        DeletedRevisionIds.AddRange(revisionIds);
        revisions.Items.RemoveAll(r => revisionIds.Contains(r.Id));
        return Task.CompletedTask;
    }

    public Task<PagedList<PublicPostRow>> ListPublishedAsync(PublicPostFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>Kontentni o'zgartirmasdan qaytaradi (pipeline Infrastructure testlarida alohida tekshiriladi).</summary>
internal sealed class PassThroughContentProcessor : IContentProcessor, IContentProcessorFactory
{
    public IReadOnlyCollection<string> Formats { get; } = ["html", "tiptap-json"];

    public Result<IContentProcessor> Get(string format) =>
        Formats.Contains(format)
            ? Result.Success<IContentProcessor>(this)
            : Result.Failure<IContentProcessor>(PostErrors.UnsupportedContentFormat.WithArgs(format));

    public Task<Result<ProcessedContent>> ProcessAsync(ContentInput input, Guid ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new ProcessedContent(input.Format, input.Body, input.Raw, input.Body, 1, [], [])));
}

internal sealed class SimpleSlugGenerator : ISlugGenerator
{
    public string Generate(string text, int maxLength = 120) =>
        string.Join('-', text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Post handler'lari uchun umumiy fake'lar va handler fabrikalari.</summary>
internal sealed class PostsTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public Guid UserId { get; } = Guid.CreateVersion7();
    public InMemoryRepository<Post> Posts { get; } = new();
    public InMemoryRepository<PostRevision> Revisions { get; } = new();
    public InMemoryReadRepository<Category> Categories { get; } = new([]);
    public InMemoryReadRepository<Tag> Tags { get; } = new([]);
    public InMemoryReadRepository<MediaFile> Media { get; } = new([]);
    public FakePostRepository PostRepository { get; }
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public ITagResolver TagResolver { get; } = Substitute.For<ITagResolver>();
    public IFileStorage Storage { get; } = Substitute.For<IFileStorage>();
    public ICacheService Cache { get; } = Substitute.For<ICacheService>();
    public MyBlog.Application.Tests.Features.FakeAuthorCache AuthorCache { get; } = new();
    public PostsOptions Options { get; } = new() { AutosaveKeep = 1, MaxRevisions = 50 };
    public FixedTimeProvider Time { get; } = new(Now);
    public PassThroughContentProcessor Content { get; } = new();

    public PostsTestContext()
    {
        PostRepository = new FakePostRepository(Revisions);
        CurrentUser.Id.Returns(UserId);
        CurrentUser.RequiredId.Returns(UserId);
        CurrentUser.IsAuthenticated.Returns(true);
        TagResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IEnumerable<string>)ci[1]).Select(_ => Guid.CreateVersion7()).ToList());
        Storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>());
    }

    public Post AddPost(string title = "Salom dunyo", string slug = "salom-dunyo", string html = "<p>Matn</p>",
        Guid? ownerId = null)
    {
        var content = PostContent.Create("html", html, null, html).Value;
        var post = Post.Create(ownerId ?? UserId, title, slug, content, 1).Value;
        Posts.Add(post);
        return post;
    }

    public static ContentInput Html(string body) => new("html", body);

    public CreatePostCommandHandler CreateHandler() => new(Posts, PostRepository, Categories, Tags, Media, Revisions, UnitOfWork,
        CurrentUser, new SimpleSlugGenerator(), Content, TagResolver, Storage);

    public UpdatePostCommandHandler UpdateHandler() => new(Posts, PostRepository, Revisions, Categories, Tags, Media, UnitOfWork,
        CurrentUser, new SimpleSlugGenerator(), Content, TagResolver, Storage, Cache, Microsoft.Extensions.Options.Options.Create(Options), Time);

    public AutosavePostCommandHandler AutosaveHandler() => new(Posts, Revisions, PostRepository, UnitOfWork, CurrentUser, Content,
        Microsoft.Extensions.Options.Options.Create(Options), Time);

    public ChangePostStatusCommandHandler StatusHandler() => new(Posts, Revisions, PostRepository, Tags, Media, UnitOfWork, Storage,
        Cache, AuthorCache, Microsoft.Extensions.Options.Options.Create(Options), Time);

    public RestorePostRevisionCommandHandler RestoreHandler() => new(Posts, Revisions, PostRepository, Tags, Media, UnitOfWork,
        CurrentUser, Content, Storage, Cache, Microsoft.Extensions.Options.Options.Create(Options), Time);

    public ScheduledPostsPublisherJob PublisherJob() => new(Posts, UnitOfWork, Cache, AuthorCache, Microsoft.Extensions.Options.Options.Create(Options),
        Time, NullLogger<ScheduledPostsPublisherJob>.Instance);

    public static UpdatePostCommand Update(Post post, string title, string html, uint? version = null, string? slug = null) =>
        new(post.Id, title, slug, post.Summary, post.CategoryId, null, post.CoverMediaId, Html(html), Version: version);
}
