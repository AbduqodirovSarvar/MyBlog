using MyBlog.Domain.Posts;
using MyBlog.Domain.Posts.Events;

namespace MyBlog.Domain.Tests.Posts;

public sealed class PostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.CreateVersion7();

    private static Post NewPost(string title = "Hello world", string slug = "hello-world")
    {
        var content = PostContent.Create("html", "<p>Hi</p>", null, "Hi").Value;
        return Post.Create(OwnerId, title, slug, content, readingTimeMinutes: 1).Value;
    }

    [Fact]
    public void Create_StartsAsDraftWithCommentsAllowed()
    {
        var post = NewPost();

        post.Status.ShouldBe(PostStatus.Draft);
        post.AllowComments.ShouldBeTrue();
        post.PublishedAt.ShouldBeNull();
        post.OwnerId.ShouldBe(OwnerId);
        post.Seo.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("", "slug", "Post.TitleRequired")]
    [InlineData("   ", "slug", "Post.TitleRequired")]
    [InlineData("Title", "Bad Slug", "Post.SlugInvalid")]
    public void Create_InvalidInput_Fails(string title, string slug, string expectedCode)
    {
        var content = PostContent.Empty();

        var result = Post.Create(OwnerId, title, slug, content, 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void Create_TitleTooLong_Fails()
    {
        var result = Post.Create(OwnerId, new string('a', PostConstraints.TitleMaxLength + 1), "slug", PostContent.Empty(), 0);

        result.Error.ShouldBe(PostErrors.TitleTooLong);
    }

    [Fact]
    public void Publish_FromDraft_SetsPublishedAtAndRaisesEvent()
    {
        var post = NewPost();

        var result = post.Publish(Now);

        result.IsSuccess.ShouldBeTrue();
        post.Status.ShouldBe(PostStatus.Published);
        post.PublishedAt.ShouldBe(Now);
        var domainEvent = post.DomainEvents.OfType<PostPublishedDomainEvent>().ShouldHaveSingleItem();
        domainEvent.PostId.ShouldBe(post.Id);
        domainEvent.IsFirstPublish.ShouldBeTrue();
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_Fails()
    {
        var post = NewPost();
        post.Publish(Now);

        post.Publish(Now.AddHours(1)).Error.ShouldBe(PostErrors.AlreadyPublished);
    }

    [Fact]
    public void Republish_KeepsOriginalPublishedAt()
    {
        var post = NewPost();
        post.Publish(Now);
        post.Unpublish();
        post.ClearDomainEvents();

        post.Publish(Now.AddDays(3)).IsSuccess.ShouldBeTrue();

        post.PublishedAt.ShouldBe(Now);
        post.DomainEvents.OfType<PostPublishedDomainEvent>().ShouldHaveSingleItem().IsFirstPublish.ShouldBeFalse();
    }

    [Fact]
    public void Unpublish_FromPublished_ReturnsToDraft()
    {
        var post = NewPost();
        post.Publish(Now);

        post.Unpublish().IsSuccess.ShouldBeTrue();

        post.Status.ShouldBe(PostStatus.Draft);
    }

    [Fact]
    public void Unpublish_FromDraft_Fails() =>
        NewPost().Unpublish().Error.ShouldBe(PostErrors.NotPublished);

    [Fact]
    public void Schedule_InFuture_SetsScheduled()
    {
        var post = NewPost();
        var at = Now.AddDays(1);

        post.Schedule(at, Now).IsSuccess.ShouldBeTrue();

        post.Status.ShouldBe(PostStatus.Scheduled);
        post.ScheduledAt.ShouldBe(at);
        post.IsDueForPublishing(Now).ShouldBeFalse();
        post.IsDueForPublishing(at).ShouldBeTrue();
    }

    [Fact]
    public void Schedule_NormalizesOffsetToUtc()
    {
        var post = NewPost();
        var at = new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.FromHours(5));

        post.Schedule(at, Now);

        post.ScheduledAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        post.ScheduledAt.ShouldBe(at);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Schedule_NotInFuture_Fails(int minutesFromNow) =>
        NewPost().Schedule(Now.AddMinutes(minutesFromNow), Now).Error.ShouldBe(PostErrors.ScheduleInPast);

    [Fact]
    public void Schedule_WhenPublished_Fails()
    {
        var post = NewPost();
        post.Publish(Now);

        post.Schedule(Now.AddDays(1), Now).Error.ShouldBe(PostErrors.CannotSchedulePublished);
    }

    [Fact]
    public void Publish_FromScheduled_ClearsScheduledAt()
    {
        var post = NewPost();
        post.Schedule(Now.AddDays(1), Now);

        post.Publish(Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        post.ScheduledAt.ShouldBeNull();
        post.Status.ShouldBe(PostStatus.Published);
    }

    [Fact]
    public void Archive_Twice_Fails()
    {
        var post = NewPost();
        post.Publish(Now);

        post.Archive().IsSuccess.ShouldBeTrue();
        post.Archive().Error.ShouldBe(PostErrors.AlreadyArchived);
        post.Publish(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void SetTags_SyncsAndDeduplicates()
    {
        var post = NewPost();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        post.SetTags([a, b, b]).IsSuccess.ShouldBeTrue();
        post.Tags.Select(t => t.TagId).ShouldBe([a, b], ignoreOrder: true);

        post.SetTags([b, c]);
        post.Tags.Select(t => t.TagId).ShouldBe([b, c], ignoreOrder: true);
        post.Tags.ShouldAllBe(t => t.PostId == post.Id);
    }

    [Fact]
    public void SetTags_TooMany_Fails()
    {
        var ids = Enumerable.Range(0, PostConstraints.MaxTags + 1).Select(_ => Guid.CreateVersion7());

        NewPost().SetTags(ids).Error.ShouldBe(PostErrors.TooManyTags);
    }

    [Fact]
    public void UpdateContent_SyncsMedia()
    {
        var post = NewPost();
        var m1 = Guid.CreateVersion7();
        var m2 = Guid.CreateVersion7();
        var content = PostContent.Create("tiptap-json", "<p>x</p>", "{\"type\":\"doc\"}", "x").Value;

        post.UpdateContent(content, 2, [m1, m2, Guid.Empty]).IsSuccess.ShouldBeTrue();
        post.Media.Select(m => m.MediaFileId).ShouldBe([m1, m2], ignoreOrder: true);
        post.Content.Format.ShouldBe("tiptap-json");
        post.ReadingTimeMinutes.ShouldBe(2);

        post.UpdateContent(content, 2, [m2]);
        post.Media.ShouldHaveSingleItem().MediaFileId.ShouldBe(m2);
    }

    [Fact]
    public void MarkDeleted_RaisesEventOnce()
    {
        var post = NewPost();

        post.MarkDeleted();
        post.MarkDeleted();

        post.IsDeleted.ShouldBeTrue();
        post.DomainEvents.OfType<PostDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Revision_CopiesTitleAndContent()
    {
        var post = NewPost();

        var revision = PostRevision.Create(post, RevisionKind.Manual, 1, Now);

        revision.PostId.ShouldBe(post.Id);
        revision.OwnerId.ShouldBe(post.OwnerId);
        revision.Title.ShouldBe(post.Title);
        revision.Content.Html.ShouldBe(post.Content.Html);
        revision.Content.ShouldNotBeSameAs(post.Content);
        revision.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void SeoMeta_InvalidCanonicalUrl_Fails() =>
        SeoMeta.Create(null, null, null, "not-a-url").Error.ShouldBe(PostErrors.CanonicalUrlInvalid);

    [Fact]
    public void PostContent_RequiresFormat() =>
        PostContent.Create(" ", "<p/>", null, "").Error.ShouldBe(PostErrors.ContentFormatRequired);
}
