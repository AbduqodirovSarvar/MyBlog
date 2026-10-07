using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Posts.Manage;
using MyBlog.Domain.Common;
using MyBlog.Domain.Posts;
using NSubstitute;

namespace MyBlog.Application.Tests.Posts;

public sealed class PostHandlerTests
{
    private readonly PostsTestContext _ctx = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Slug ----------

    [Fact]
    public async Task Create_generates_slug_from_title_and_appends_suffix_when_taken()
    {
        _ctx.AddPost(title: "Salom dunyo", slug: "salom-dunyo");
        _ctx.AddPost(title: "Salom dunyo", slug: "salom-dunyo-2");

        var result = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("Salom dunyo", null, null, null, null, null, PostsTestContext.Html("<p>a</p>")), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Slug.ShouldBe("salom-dunyo-3");
        result.Value.Status.ShouldBe(PostStatus.Draft);
        _ctx.Posts.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Create_rejects_explicit_slug_already_used_by_same_author()
    {
        _ctx.AddPost(slug: "mening-postim");

        var result = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("Boshqa", "mening-postim", null, null, null, null, PostsTestContext.Html("<p>a</p>")), Ct);

        result.Error.ShouldBe(PostErrors.SlugTaken);
    }

    [Fact]
    public async Task Slug_of_another_author_does_not_conflict()
    {
        _ctx.AddPost(slug: "umumiy", ownerId: Guid.CreateVersion7());

        var result = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("Umumiy", null, null, null, null, null, PostsTestContext.Html("<p>a</p>")), Ct);

        result.Value.Slug.ShouldBe("umumiy");
    }

    [Fact]
    public async Task Create_resolves_tags_and_rejects_too_many()
    {
        var ok = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("Teglar", null, null, null, ["dotnet", "DotNet", " efcore "], null, PostsTestContext.Html("<p>a</p>")), Ct);

        ok.IsSuccess.ShouldBeTrue();
        _ctx.Posts.Items.Single().Tags.Count.ShouldBe(2);

        var tooMany = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("Ko'p", null, null, null, Enumerable.Range(0, 11).Select(i => $"t{i}").ToList(), null,
                PostsTestContext.Html("<p>a</p>")), Ct);

        tooMany.Error.Code.ShouldBe(PostErrors.TooManyTags.Code);
    }

    [Fact]
    public async Task Create_with_unknown_format_fails()
    {
        var result = await _ctx.CreateHandler().Handle(
            new CreatePostCommand("X", null, null, null, null, null, new("markdown-x", "<p>a</p>")), Ct);

        result.Error.Code.ShouldBe("Post.UnsupportedContentFormat");
    }

    // ---------- Reviziyalar ----------

    [Fact]
    public async Task Update_with_changed_content_creates_manual_revision_of_previous_state()
    {
        var post = _ctx.AddPost(title: "Eski", html: "<p>eski</p>");

        var result = await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "Yangi", "<p>yangi</p>"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var revision = _ctx.Revisions.Items.ShouldHaveSingleItem();
        revision.Kind.ShouldBe(RevisionKind.Manual);
        revision.RevisionNumber.ShouldBe(1);
        revision.Title.ShouldBe("Eski");
        revision.Content.Html.ShouldBe("<p>eski</p>");
        post.Title.ShouldBe("Yangi");
        post.Content.Html.ShouldBe("<p>yangi</p>");
        await _ctx.Cache.Received().RemoveByTagAsync(PostCache.Tag(post.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_without_content_change_creates_no_revision_and_keeps_slug()
    {
        var post = _ctx.AddPost(title: "Bir xil", slug: "bir-xil", html: "<p>x</p>");

        var result = await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "Bir xil", "<p>x</p>"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _ctx.Revisions.Items.ShouldBeEmpty();
        post.Slug.ShouldBe("bir-xil");
    }

    [Fact]
    public async Task Revisions_beyond_limit_are_pruned_oldest_first()
    {
        _ctx.Options.MaxRevisions = 3;
        var post = _ctx.AddPost(html: "<p>v0</p>");

        for (var i = 1; i <= 5; i++)
            (await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "T", $"<p>v{i}</p>"), Ct)).IsSuccess.ShouldBeTrue();

        _ctx.Revisions.Items.Select(r => r.RevisionNumber).Order().ShouldBe([3, 4, 5]);
        _ctx.Revisions.Items.Single(r => r.RevisionNumber == 5).Content.Html.ShouldBe("<p>v4</p>");
    }

    [Fact]
    public async Task Autosave_keeps_only_latest_autosave_and_does_not_touch_post()
    {
        var post = _ctx.AddPost(title: "Asl", html: "<p>asl</p>");

        var first = await _ctx.AutosaveHandler().Handle(new AutosavePostCommand(post.Id, "Qoralama 1", PostsTestContext.Html("<p>1</p>")), Ct);
        var second = await _ctx.AutosaveHandler().Handle(new AutosavePostCommand(post.Id, "Qoralama 2", PostsTestContext.Html("<p>2</p>")), Ct);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        var autosave = _ctx.Revisions.Items.ShouldHaveSingleItem();
        autosave.Kind.ShouldBe(RevisionKind.Autosave);
        autosave.Id.ShouldBe(second.Value.RevisionId);
        autosave.Title.ShouldBe("Qoralama 2");
        autosave.Content.Html.ShouldBe("<p>2</p>");

        post.Title.ShouldBe("Asl");
        post.Content.Html.ShouldBe("<p>asl</p>");
    }

    [Fact]
    public async Task Manual_save_clears_pending_autosaves()
    {
        var post = _ctx.AddPost(html: "<p>asl</p>");
        await _ctx.AutosaveHandler().Handle(new AutosavePostCommand(post.Id, "Q", PostsTestContext.Html("<p>q</p>")), Ct);

        await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "Q", "<p>q</p>"), Ct);

        _ctx.Revisions.Items.ShouldAllBe(r => r.Kind == RevisionKind.Manual);
    }

    [Fact]
    public async Task Restore_applies_revision_and_snapshots_current_state()
    {
        var post = _ctx.AddPost(title: "V1", html: "<p>v1</p>");
        await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "V2", "<p>v2</p>"), Ct);
        var v1 = _ctx.Revisions.Items.Single();

        var result = await _ctx.RestoreHandler().Handle(new RestorePostRevisionCommand(post.Id, v1.Id), Ct);

        result.IsSuccess.ShouldBeTrue();
        post.Title.ShouldBe("V1");
        post.Content.Html.ShouldBe("<p>v1</p>");
        _ctx.Revisions.Items.OrderBy(r => r.RevisionNumber).Last().Title.ShouldBe("V2");
    }

    [Fact]
    public void Pruning_selection_respects_autosave_keep_and_max_revisions()
    {
        RevisionHeader H(int n, RevisionKind kind) => new(Guid.CreateVersion7(), n, kind, "t", PostsTestContext.Now);
        var existing = new List<RevisionHeader>
        {
            H(1, RevisionKind.Manual), H(2, RevisionKind.Autosave), H(3, RevisionKind.Manual), H(4, RevisionKind.Autosave)
        };

        // AutosaveKeep=2: yangi autosave bilan 2 ta qolishi uchun eng eski autosave (2) o'chadi.
        PostRevisionWriter.SelectForDeletion(existing, RevisionKind.Autosave, false, autosaveKeep: 2, maxRevisions: 10)
            .ShouldBe([existing[1].Id]);

        // Max=3: (1,3,4 qoladi → +1 yangi = 4) → eng eskisi (1) ham o'chadi.
        PostRevisionWriter.SelectForDeletion(existing, RevisionKind.Autosave, false, autosaveKeep: 2, maxRevisions: 3)
            .ShouldBe([existing[1].Id, existing[0].Id]);

        // Qo'lda saqlash autosave'larni tozalaydi.
        PostRevisionWriter.SelectForDeletion(existing, RevisionKind.Manual, true, autosaveKeep: 1, maxRevisions: 10)
            .ShouldBe([existing[1].Id, existing[3].Id]);
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task Update_with_stale_version_returns_conflict()
    {
        var post = _ctx.AddPost();
        _ctx.PostRepository.Versions[post.Id] = 7;

        var stale = await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "Yangi", "<p>y</p>", version: 6), Ct);
        stale.Error.ShouldBe(PostErrors.VersionConflict);

        var fresh = await _ctx.UpdateHandler().Handle(PostsTestContext.Update(post, "Yangi", "<p>y</p>", version: 7), Ct);
        fresh.IsSuccess.ShouldBeTrue();
        fresh.Value.Version.ShouldBe(7u);
        _ctx.PostRepository.ExpectedVersions[post.Id].ShouldBe(7u);
    }

    // ---------- Holatlar ----------

    [Fact]
    public async Task Publish_transitions_and_records_publish_revision()
    {
        var post = _ctx.AddPost();

        var published = await _ctx.StatusHandler().Handle(new ChangePostStatusCommand(post.Id, PostStatusAction.Publish), Ct);
        published.IsSuccess.ShouldBeTrue();
        published.Value.Status.ShouldBe(PostStatus.Published);
        published.Value.PublishedAt.ShouldBe(PostsTestContext.Now);
        _ctx.Revisions.Items.ShouldHaveSingleItem().Kind.ShouldBe(RevisionKind.Publish);
        await _ctx.Cache.Received().RemoveByTagAsync(PostCache.Tag(post.Id), Arg.Any<CancellationToken>());
        _ctx.AuthorCache.InvalidatedUsers.ShouldContain(post.OwnerId);

        var again = await _ctx.StatusHandler().Handle(new ChangePostStatusCommand(post.Id, PostStatusAction.Publish), Ct);
        again.Error.ShouldBe(PostErrors.AlreadyPublished);

        var unpublished = await _ctx.StatusHandler().Handle(new ChangePostStatusCommand(post.Id, PostStatusAction.Unpublish), Ct);
        unpublished.Value.Status.ShouldBe(PostStatus.Draft);
    }

    [Fact]
    public async Task Schedule_in_past_is_rejected_and_future_is_accepted()
    {
        var post = _ctx.AddPost();

        var past = await _ctx.StatusHandler().Handle(
            new ChangePostStatusCommand(post.Id, PostStatusAction.Schedule, PostsTestContext.Now.AddMinutes(-1)), Ct);
        past.Error.ShouldBe(PostErrors.ScheduleInPast);

        var future = await _ctx.StatusHandler().Handle(
            new ChangePostStatusCommand(post.Id, PostStatusAction.Schedule, PostsTestContext.Now.AddHours(1)), Ct);
        future.Value.Status.ShouldBe(PostStatus.Scheduled);
        future.Value.ScheduledAt.ShouldBe(PostsTestContext.Now.AddHours(1));
    }

    [Fact]
    public async Task Status_change_of_missing_post_returns_not_found()
    {
        var result = await _ctx.StatusHandler().Handle(new ChangePostStatusCommand(Guid.CreateVersion7(), PostStatusAction.Archive), Ct);

        result.Error.ShouldBe(PostErrors.NotFound);
    }

    [Fact]
    public async Task Scheduler_job_publishes_due_posts_only()
    {
        var due = _ctx.AddPost(slug: "due");
        var later = _ctx.AddPost(slug: "later");
        due.Schedule(PostsTestContext.Now.AddMinutes(5), PostsTestContext.Now).IsSuccess.ShouldBeTrue();
        later.Schedule(PostsTestContext.Now.AddHours(5), PostsTestContext.Now).IsSuccess.ShouldBeTrue();

        _ctx.Time.Now = PostsTestContext.Now.AddMinutes(10);
        await _ctx.PublisherJob().ExecuteAsync(Ct);

        due.Status.ShouldBe(PostStatus.Published);
        due.PublishedAt.ShouldBe(PostsTestContext.Now.AddMinutes(10));
        later.Status.ShouldBe(PostStatus.Scheduled);
        _ctx.UnitOfWork.SaveCount.ShouldBe(1);
    }
}
