using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Reactions.Common;
using MyBlog.Domain.Reactions;

namespace MyBlog.Api.IntegrationTests.Comments;

/// <summary>Izohlar va reaksiyalar: to'liq HTTP oqimi va parallel reaksiyalar (haqiqiy PostgreSQL'da).</summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentsAndReactionsTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    [Fact]
    public async Task Comment_reply_like_and_delete_flow_keeps_counters_consistent()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres);
        using var alice = host.Client(host.AliceId);
        using var bob = host.Client(host.BobId);
        using var owner = host.Client(host.OwnerId);
        using var anonymous = host.Client(null);

        // Izoh va javob
        var createRoot = await alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "  Ajoyib post!  " }, Ct);
        createRoot.StatusCode.ShouldBe(HttpStatusCode.Created);
        var root = await ReadAsync<CommentDto>(createRoot);
        root.Content.ShouldBe("Ajoyib post!");
        root.Author!.Username.ShouldBe("alice");
        createRoot.Headers.Location!.ToString().ShouldBe($"/api/comments/{root.Id}");

        var reply = await ReadAsync<CommentDto>(
            await bob.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "Rahmat", parentId = root.Id }, Ct));
        reply.Depth.ShouldBe(1);
        reply.ParentId.ShouldBe(root.Id);

        // Izohga va postga reaksiyalar
        var commentLike = await ReadAsync<ReactionSummaryDto>(
            await owner.PutAsJsonAsync($"/api/comments/{root.Id}/reaction", new { type = "Like" }, Ct));
        commentLike.ShouldBe(new ReactionSummaryDto(1, 0, "Like"));

        (await ReadAsync<ReactionSummaryDto>(await bob.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)))
            .ShouldBe(new ReactionSummaryDto(1, 0, "Like"));
        (await ReadAsync<ReactionSummaryDto>(await alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Dislike" }, Ct)))
            .ShouldBe(new ReactionSummaryDto(1, 1, "Dislike"));
        (await ReadAsync<ReactionSummaryDto>(await alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)))
            .ShouldBe(new ReactionSummaryDto(2, 0, "Like"));
        (await ReadAsync<ReactionSummaryDto>(await alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)))
            .ShouldBe(new ReactionSummaryDto(2, 0, "Like"));
        (await ReadAsync<ReactionSummaryDto>(await bob.DeleteAsync($"/api/posts/{host.PostId}/reaction", Ct)))
            .ShouldBe(new ReactionSummaryDto(1, 0, null));

        // O'qish: daraxt, hisoblagichlar, mening reaksiyam
        var page = await ReadAsync<PagedList<CommentDto>>(await owner.GetAsync($"/api/posts/{host.PostId}/comments?sort=newest", Ct));
        var listedRoot = page.Items.ShouldHaveSingleItem();
        listedRoot.LikeCount.ShouldBe(1);
        listedRoot.MyReaction.ShouldBe("Like");
        listedRoot.CanDelete.ShouldBeTrue(); // post egasi
        listedRoot.CanEdit.ShouldBeFalse();
        listedRoot.Replies.ShouldHaveSingleItem().Id.ShouldBe(reply.Id);

        (await ReadAsync<CommentCountDto>(await anonymous.GetAsync($"/api/posts/{host.PostId}/comments/count", Ct))).Count.ShouldBe(2);
        var post = await host.LoadPostAsync();
        (post.CommentCount, post.LikeCount, post.DislikeCount).ShouldBe((2, 1, 0));

        // Tahrirlash: faqat muallif
        (await bob.PutAsJsonAsync($"/api/comments/{root.Id}", new { content = "Hack" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var edited = await ReadAsync<CommentDto>(await alice.PutAsJsonAsync($"/api/comments/{root.Id}", new { content = "Tahrirlandi" }, Ct));
        edited.IsEdited.ShouldBeTrue();

        // Javobi bor izoh o'chirilsa placeholder qoladi
        (await bob.DeleteAsync($"/api/comments/{root.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await alice.DeleteAsync($"/api/comments/{root.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        page = await ReadAsync<PagedList<CommentDto>>(await anonymous.GetAsync($"/api/posts/{host.PostId}/comments", Ct));
        var placeholder = page.Items.ShouldHaveSingleItem();
        placeholder.Deleted.ShouldBeTrue();
        placeholder.Content.ShouldBeNull();
        placeholder.Author.ShouldBeNull();
        placeholder.Replies.ShouldHaveSingleItem().Content.ShouldBe("Rahmat");
        (await ReadAsync<CommentCountDto>(await anonymous.GetAsync($"/api/posts/{host.PostId}/comments/count", Ct))).Count.ShouldBe(1);

        // O'chirilgan izohga javob ham, reaksiya ham bo'lmaydi
        (await bob.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "?", parentId = root.Id }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await bob.PutAsJsonAsync($"/api/comments/{root.Id}/reaction", new { type = "Like" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Oxirgi javob o'chirilsa butun shoxcha yo'qoladi
        (await bob.DeleteAsync($"/api/comments/{reply.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        page = await ReadAsync<PagedList<CommentDto>>(await anonymous.GetAsync($"/api/posts/{host.PostId}/comments", Ct));
        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
        (await host.LoadPostAsync()).CommentCount.ShouldBe(0);
    }

    [Fact]
    public async Task Authorization_and_validation_are_enforced()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres);
        using var anonymous = host.Client(null);
        using var readOnly = host.Client(host.AliceId, "Profile.Manage");
        using var alice = host.Client(host.AliceId);
        using var moderator = host.Client(host.BobId, Permissions.Comments.Moderate);

        (await anonymous.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "x" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await readOnly.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "x" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await readOnly.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "   " }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await alice.PostAsJsonAsync($"/api/posts/{Guid.NewGuid()}/comments", new { content = "x" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Love" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var comment = await ReadAsync<CommentDto>(
            await alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "Spam?" }, Ct));

        var mine = await ReadAsync<PagedList<CommentListItemDto>>(await alice.GetAsync("/api/my/comments", Ct));
        mine.Items.ShouldHaveSingleItem().Post.AuthorUsername.ShouldBe("owner");

        (await alice.GetAsync("/api/admin/comments", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var found = await ReadAsync<PagedList<CommentListItemDto>>(await moderator.GetAsync("/api/admin/comments?search=spam", Ct));
        found.Items.ShouldHaveSingleItem().Id.ShouldBe(comment.Id);
        (await moderator.DeleteAsync($"/api/admin/comments/{comment.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.LoadPostAsync()).CommentCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_reactions_keep_counters_consistent()
    {
        postgres.SkipIfUnavailable();
        const int users = 8;
        await using var host = await CommentsTestHost.StartAsync(postgres, extraUsers: users);

        // Bir xil foydalanuvchi bir vaqtda ko'p marta Like bosadi → bitta reaksiya, LikeCount = 1.
        using var alice = host.Client(host.AliceId);
        var sameUser = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            alice.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct)));
        sameUser.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await host.LoadPostAsync()).LikeCount.ShouldBe(1);

        // Turli foydalanuvchilar parallel → har biri hisoblanadi.
        var clients = host.ExtraUserIds.Select(id => host.Client(id)).ToList();
        var manyUsers = await Task.WhenAll(clients.Select(c =>
            c.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Dislike" }, Ct)));
        manyUsers.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        // Bitta foydalanuvchi parallel Like/Dislike/Remove aralash — yakuniy hisoblagichlar jadvaldagi qatorlarga mos.
        using var bob = host.Client(host.BobId);
        var mixed = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => (i % 3) switch
        {
            0 => bob.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Like" }, Ct),
            1 => bob.PutAsJsonAsync($"/api/posts/{host.PostId}/reaction", new { type = "Dislike" }, Ct),
            _ => bob.DeleteAsync($"/api/posts/{host.PostId}/reaction", Ct)
        }));
        mixed.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);

        var post = await host.LoadPostAsync();
        var (likes, dislikes) = await host.QueryAsync(async db =>
        {
            var rows = await db.Set<Reaction>()
                .Where(r => r.TargetType == ReactionTargetType.Post && r.TargetId == host.PostId)
                .ToListAsync(Ct);
            return (rows.Count(r => r.Type == ReactionType.Like), rows.Count(r => r.Type == ReactionType.Dislike));
        });

        (post.LikeCount, post.DislikeCount).ShouldBe((likes, dislikes));
        dislikes.ShouldBeGreaterThanOrEqualTo(users);

        foreach (var client in clients)
            client.Dispose();
    }
}
