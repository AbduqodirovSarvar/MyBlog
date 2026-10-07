using System.Net;
using System.Net.Http.Json;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Authors.Common;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Profile.Common;

namespace MyBlog.Api.IntegrationTests.Profile;

[Collection(PostgresCollection.Name)]
public sealed class PublicAuthorsTests(PostgresFixture postgres)
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    [Fact]
    public async Task Anonymous_user_reads_author_list_and_profile()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"), (Bob, "bob"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);
        using var anonymous = host.Anonymous();

        (await alice.PutAsJsonAsync("/api/my/profile/basic", new { firstName = "Alisa", lastName = "Karimova", bio = "Backend" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await alice.PostAsJsonAsync("/api/my/profile/skills", new { name = "C#", level = (int?)null }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await alice.PostAsJsonAsync("/api/my/profile/skills", new { name = "SQL", level = 150 }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var own = await alice.GetFromJsonAsync<ProfileDto>("/api/my/profile", ct);
        own!.DisplayName.ShouldBe("Alisa Karimova");
        own.Skills.ShouldHaveSingleItem().Level.ShouldBeNull();

        var list = await anonymous.GetFromJsonAsync<PagedList<AuthorSummaryDto>>("/api/public/authors?search=alis", ct);
        var summary = list!.Items.ShouldHaveSingleItem();
        summary.Username.ShouldBe("alice");
        summary.PublishedPostCount.ShouldBe(0);

        var profile = await anonymous.GetFromJsonAsync<AuthorProfileDto>("/api/public/authors/ALICE", ct);
        profile!.DisplayName.ShouldBe("Alisa Karimova");
        profile.Skills.ShouldHaveSingleItem().Name.ShouldBe("C#");

        (await anonymous.GetAsync("/api/public/authors/nobody", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/my/profile", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Public_profile_cache_is_invalidated_on_update()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);
        using var anonymous = host.Anonymous();

        (await anonymous.GetFromJsonAsync<AuthorProfileDto>("/api/public/authors/alice", ct))!.Bio.ShouldBeNull();

        await alice.PutAsJsonAsync("/api/my/profile/basic", new { bio = "Yangi bio" }, ct);

        (await anonymous.GetFromJsonAsync<AuthorProfileDto>("/api/public/authors/alice", ct))!.Bio.ShouldBe("Yangi bio");
    }

    [Fact]
    public async Task Public_categories_are_active_only_and_localized()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);
        using var anonymous = host.Anonymous();

        await alice.PostAsJsonAsync("/api/my/categories", new
        {
            translations = new[] { new { culture = "uz", name = "Texnologiya" }, new { culture = "ru", name = "Технологии" } }
        }, ct);
        await alice.PostAsJsonAsync("/api/my/categories", new
        {
            translations = new[] { new { culture = "uz", name = "Yashirin" } },
            isActive = false
        }, ct);

        var ru = await anonymous.GetFromJsonAsync<List<PublicCategoryDto>>("/api/public/authors/alice/categories?culture=ru", ct);
        ru.ShouldHaveSingleItem().Name.ShouldBe("Технологии");

        var en = await anonymous.GetFromJsonAsync<List<PublicCategoryDto>>("/api/public/authors/alice/categories?culture=en", ct);
        en.ShouldHaveSingleItem().Name.ShouldBe("Texnologiya");

        (await anonymous.GetFromJsonAsync<List<object>>("/api/public/authors/alice/tags", ct)).ShouldBeEmpty();
    }
}
