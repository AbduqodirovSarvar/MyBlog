using System.Net;
using System.Net.Http.Json;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Features.Categories.Common;

namespace MyBlog.Api.IntegrationTests.Profile;

[Collection(PostgresCollection.Name)]
public sealed class CategoryIsolationTests(PostgresFixture postgres)
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private static object NewCategory(string uzName, Guid? parentId = null) => new
    {
        translations = new[] { new { culture = "uz", name = uzName }, new { culture = "ru", name = uzName + " (ru)" } },
        parentId
    };

    [Fact]
    public async Task Other_user_cannot_see_or_modify_category()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"), (Bob, "bob"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);
        using var bob = host.As(Bob);

        var created = await alice.PostAsJsonAsync("/api/my/categories", NewCategory("Yangiliklar"), ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var category = (await created.Content.ReadFromJsonAsync<CategoryDto>(ct))!;
        category.Slug.ShouldBe("yangiliklar");

        (await bob.GetAsync($"/api/my/categories/{category.Id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.PutAsJsonAsync($"/api/my/categories/{category.Id}",
            new { translations = new[] { new { culture = "uz", name = "Hack" } }, isActive = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.PatchAsJsonAsync($"/api/my/categories/{category.Id}/move", new { newParentId = (Guid?)null, order = 0 }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.DeleteAsync($"/api/my/categories/{category.Id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<List<CategoryDto>>("/api/my/categories", ct)).ShouldBeEmpty();

        // Bob boshqaning kategoriyasini ota qilib ham ishlata olmaydi
        (await bob.PostAsJsonAsync("/api/my/categories", NewCategory("Bola", category.Id), ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var own = await alice.GetFromJsonAsync<CategoryDto>($"/api/my/categories/{category.Id}", ct);
        own!.Translations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Same_slug_for_different_users_and_suffix_for_same_user()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"), (Bob, "bob"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);
        using var bob = host.As(Bob);

        async Task<string> CreateAsync(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("/api/my/categories", NewCategory("Dasturlash"), ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<CategoryDto>(ct))!.Slug;
        }

        (await CreateAsync(alice)).ShouldBe("dasturlash");
        (await CreateAsync(bob)).ShouldBe("dasturlash");
        (await CreateAsync(alice)).ShouldBe("dasturlash-2");
    }

    [Fact]
    public async Task Tree_move_and_delete_rules()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres, (Alice, "alice"));
        var ct = TestContext.Current.CancellationToken;
        using var alice = host.As(Alice);

        async Task<CategoryDto> CreateAsync(string name, Guid? parentId = null) =>
            (await (await alice.PostAsJsonAsync("/api/my/categories", NewCategory(name, parentId), ct))
                .Content.ReadFromJsonAsync<CategoryDto>(ct))!;

        var root = await CreateAsync("Root");
        var child = await CreateAsync("Child", root.Id);

        (await alice.PatchAsJsonAsync($"/api/my/categories/{root.Id}/move", new { newParentId = child.Id, order = 0 }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await alice.DeleteAsync($"/api/my/categories/{root.Id}", ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var tree = await alice.GetFromJsonAsync<List<CategoryDto>>("/api/my/categories", ct);
        tree.ShouldHaveSingleItem().Children.ShouldHaveSingleItem().Id.ShouldBe(child.Id);

        (await alice.DeleteAsync($"/api/my/categories/{child.Id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await alice.DeleteAsync($"/api/my/categories/{root.Id}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await alice.GetFromJsonAsync<List<CategoryDto>>("/api/my/categories", ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Anonymous_user_cannot_manage_categories()
    {
        postgres.SkipIfUnavailable();
        await using var host = await ProfileApiTestHost.CreateAsync(postgres);
        using var anonymous = host.Anonymous();

        (await anonymous.GetAsync("/api/my/categories", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
