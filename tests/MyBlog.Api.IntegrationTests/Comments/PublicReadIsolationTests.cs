using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Reactions.Common;
using MyBlog.Domain.Comments;

namespace MyBlog.Api.IntegrationTests.Comments;

/// <summary>
/// DataIsolation:PublicReadOfPublishedContent: true — ommaviy o'qish (default), false — yopiq tizim: boshqaning kontenti 404,
/// o'z ma'lumotlari (api/my/..., o'z postidagi izoh/reaksiya) ishlayveradi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublicReadIsolationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] OwnerPermissions =
        [Permissions.Comments.Write, Permissions.Reactions.Write, Permissions.Posts.Manage, Permissions.Profile.Manage];

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task ShouldBeNotFoundAsync(Task<HttpResponseMessage> request, string? code = null)
    {
        using var response = await request;
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{response.RequestMessage?.RequestUri}: {body}");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        if (code is not null)
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private static readonly string[] PublicUrls =
    [
        "/api/public/posts",
        "/api/public/posts?author=owner",
        "/api/public/authors/owner/posts/integration-post",
        "/api/public/authors",
        "/api/public/authors/owner",
        "/api/public/authors/owner/categories",
        "/api/public/authors/owner/tags"
    ];

    [Fact]
    public async Task Open_system_exposes_published_content_to_everyone()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres);
        using var anonymous = host.Client(null);
        using var alice = host.Client(host.AliceId);

        foreach (var url in PublicUrls)
            (await anonymous.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);

        var list = await ReadAsync<PagedList<PublicPostSummaryDto>>(await anonymous.GetAsync("/api/public/posts", Ct));
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(host.PostId);

        var comment = await ReadAsync<CommentDto>(
            await alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "Ochiq" }, Ct));
        (await anonymous.GetAsync($"/api/comments/{comment.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<ReactionSummaryDto>(await alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)))
            .LikeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Closed_system_hides_foreign_content_but_keeps_own_data_working()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres, publicRead: false);
        using var anonymous = host.Client(null);
        using var alice = host.Client(host.AliceId);
        using var owner = host.Client(host.OwnerId, OwnerPermissions);

        // Ommaviy sirt butunlay yopiq — anonim, boshqa foydalanuvchi va hatto egasi uchun ham (o'zi api/my orqali ishlaydi).
        foreach (var url in PublicUrls)
        {
            await ShouldBeNotFoundAsync(anonymous.GetAsync(url, Ct), "General.NotFound");
            await ShouldBeNotFoundAsync(alice.GetAsync(url, Ct), "General.NotFound");
            await ShouldBeNotFoundAsync(owner.GetAsync(url, Ct), "General.NotFound");
        }

        // Egasi o'z postida izoh yozadi va reaksiya qo'yadi.
        var ownComment = await ReadAsync<CommentDto>(
            await owner.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "O'z postimga izoh" }, Ct));
        (await ReadAsync<PagedList<CommentDto>>(await owner.GetAsync($"/api/posts/{host.PostId}/comments", Ct)))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(ownComment.Id);
        (await owner.GetAsync($"/api/comments/{ownComment.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<ReactionSummaryDto>(await owner.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)))
            .LikeCount.ShouldBe(1);
        (await ReadAsync<ReactionSummaryDto>(await owner.PutAsJsonAsync($"/api/comments/{ownComment.Id}/reaction", new { type = "Like" }, Ct)))
            .LikeCount.ShouldBe(1);

        // Boshqa foydalanuvchi va anonim uchun bu post va uning izohlari "mavjud emas".
        foreach (var client in new[] { alice, anonymous })
        {
            await ShouldBeNotFoundAsync(client.GetAsync($"/api/posts/{host.PostId}/comments", Ct));
            await ShouldBeNotFoundAsync(client.GetAsync($"/api/posts/{host.PostId}/comments/count", Ct));
            await ShouldBeNotFoundAsync(client.GetAsync($"/api/comments/{ownComment.Id}", Ct));
        }

        await ShouldBeNotFoundAsync(alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "Salom" }, Ct));
        await ShouldBeNotFoundAsync(alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct));
        await ShouldBeNotFoundAsync(alice.DeleteAsync($"/api/posts/{host.PostId}/reaction", Ct));
        await ShouldBeNotFoundAsync(alice.PutAsJsonAsync($"/api/comments/{ownComment.Id}/reaction", new { type = "Like" }, Ct));
        await ShouldBeNotFoundAsync(alice.PutAsJsonAsync($"/api/comments/{ownComment.Id}", new { content = "X" }, Ct));
        await ShouldBeNotFoundAsync(alice.DeleteAsync($"/api/comments/{ownComment.Id}", Ct));

        var post = await host.LoadPostAsync();
        post.CommentCount.ShouldBe(1);
        post.LikeCount.ShouldBe(1);

        // O'z ma'lumotlarini boshqarish endpoint'lari ishlaydi.
        (await owner.GetAsync("/api/my/posts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync("/api/my/profile", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<PagedList<CommentListItemDto>>(await owner.GetAsync("/api/my/comments", Ct)))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(ownComment.Id);
        (await ReadAsync<PagedList<CommentListItemDto>>(await alice.GetAsync("/api/my/comments", Ct)))
            .Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Closed_system_hides_own_old_comments_on_foreign_posts()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres, publicRead: false);
        using var alice = host.Client(host.AliceId);
        using var owner = host.Client(host.OwnerId);

        // Tizim ochiq bo'lgan paytda Alice owner postiga yozgan izoh (bazaga to'g'ridan-to'g'ri).
        var oldComment = Comment.Create(host.PostId, host.AliceId, "Eski izoh").Value;
        await host.QueryAsync(async db =>
        {
            db.Add(oldComment);
            return await db.SaveChangesAsync(Ct);
        });

        // Alice uchun boshqaning posti "mavjud emas": izohi ham ko'rinmaydi, tahrirlanmaydi, o'chirilmaydi.
        (await ReadAsync<PagedList<CommentListItemDto>>(await alice.GetAsync("/api/my/comments", Ct))).Items.ShouldBeEmpty();
        await ShouldBeNotFoundAsync(alice.GetAsync($"/api/comments/{oldComment.Id}", Ct));
        await ShouldBeNotFoundAsync(alice.PutAsJsonAsync($"/api/comments/{oldComment.Id}", new { content = "Yangi" }, Ct));
        await ShouldBeNotFoundAsync(alice.DeleteAsync($"/api/comments/{oldComment.Id}", Ct));

        // Post egasi o'z postidagi izohni ko'radi va o'chira oladi.
        (await ReadAsync<PagedList<CommentDto>>(await owner.GetAsync($"/api/posts/{host.PostId}/comments", Ct)))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(oldComment.Id);
        (await owner.DeleteAsync($"/api/comments/{oldComment.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
