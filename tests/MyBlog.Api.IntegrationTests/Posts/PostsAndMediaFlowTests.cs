using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.Persistence;
using SkiaSharp;

namespace MyBlog.Api.IntegrationTests.Posts;

/// <summary>Yuklash → post → nashr → public sahifa; izolyatsiya; full-text qidiruv. Docker bo'lmasa skip.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostsAndMediaFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Guid _alice = Guid.CreateVersion7();
    private readonly Guid _bob = Guid.CreateVersion7();
    private PostsApiFactory? _factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PostsApiFactory Factory => _factory ?? throw new InvalidOperationException("PostgreSQL is not available.");

    public async ValueTask InitializeAsync()
    {
        if (postgres.ConnectionString is null)
            return;

        _factory = new PostsApiFactory(postgres.ConnectionStringFor($"posts_{Guid.NewGuid():N}"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.Set<UserProfile>().AddRange(UserProfile.Create(_alice, "alice").Value, UserProfile.Create(_bob, "bob").Value);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is null)
            return;

        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();

        await _factory.DisposeAsync();
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).Clone();

    private static async Task<JsonElement> UploadAsync(HttpClient client, byte[] bytes, string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "rasm.png");
        form.Add(new StringContent("Tog' manzarasi"), "altText");

        var response = await client.PostAsync("/api/my/media", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> CreatePostAsync(HttpClient client, string title, string body)
    {
        var response = await client.PostAsJsonAsync("/api/my/posts", new
        {
            title,
            summary = "Qisqacha",
            tags = Array.Empty<string>(),
            content = new { format = "tiptap-json", body, raw = new { type = "doc", content = Array.Empty<object>() } },
            allowComments = true
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await ReadJsonAsync(response);
    }

    [Fact]
    public async Task Uploaded_image_in_published_post_is_rendered_publicly()
    {
        postgres.SkipIfUnavailable();
        using var alice = Factory.CreateClientFor(_alice);
        using var anonymous = Factory.CreateClientFor(null);

        var media = await UploadAsync(alice, CreatePng(1200, 600));
        var mediaId = media.GetProperty("id").GetGuid();
        var mediaUrl = media.GetProperty("url").GetString()!;
        media.GetProperty("width").GetInt32().ShouldBe(1200);
        media.GetProperty("variants").GetProperty("thumb").GetProperty("width").GetInt32().ShouldBe(320);
        media.GetProperty("variants").GetProperty("medium").GetProperty("width").GetInt32().ShouldBe(960);

        var body = $"<figure class=\"image\" style=\"width: 50%; float: right\"><img src=\"https://evil.example/x.png\" data-media-id=\"{mediaId}\">" +
                   "<figcaption>Rasm</figcaption></figure><h2>Kirish</h2><p>Zanjabil juda foydali o'simlik.</p><script>alert(1)</script>";
        var post = await CreatePostAsync(alice, "Zanjabil haqida", body);
        var postId = post.GetProperty("id").GetGuid();
        var slug = post.GetProperty("slug").GetString();
        post.GetProperty("status").GetString().ShouldBe("Draft");
        post.GetProperty("content").GetProperty("raw").GetProperty("type").GetString().ShouldBe("doc");

        // Qoralama public emas.
        (await anonymous.GetAsync($"/api/public/authors/alice/posts/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var publish = await alice.PostAsync($"/api/my/posts/{postId}/publish", null, Ct);
        publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Ct));

        var detailResponse = await anonymous.GetAsync($"/api/public/authors/alice/posts/{slug}", Ct);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync(Ct));
        var detail = await ReadJsonAsync(detailResponse);
        var html = detail.GetProperty("html").GetString()!;
        html.ShouldContain($"src=\"{mediaUrl}\"");
        html.ShouldContain("float: right");
        html.ShouldContain("srcset=");
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("<script");
        detail.GetProperty("toc")[0].GetProperty("id").GetString().ShouldBe("kirish");
        detail.GetProperty("author").GetProperty("username").GetString().ShouldBe("alice");
        detail.GetProperty("myReaction").ValueKind.ShouldBe(JsonValueKind.Null);

        // Media fayli anonim, immutable kesh va ETag bilan.
        var file = await anonymous.GetAsync(mediaUrl, Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        file.Headers.CacheControl!.ToString().ShouldContain("immutable");
        var etag = file.Headers.ETag.ShouldNotBeNull();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        conditional.Headers.IfNoneMatch.Add(etag);
        (await anonymous.SendAsync(conditional, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified);

        (await anonymous.GetAsync("/media/2026/01/missing.webp", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/media/..%2F..%2Fappsettings.json", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Postda ishlatilayotgan media o'chirilmaydi.
        var delete = await alice.DeleteAsync($"/api/my/media/{mediaId}", Ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(delete)).GetProperty("code").GetString().ShouldBe("Media.InUse");
    }

    [Fact]
    public async Task Other_users_cannot_see_or_change_foreign_posts_and_media()
    {
        postgres.SkipIfUnavailable();
        using var alice = Factory.CreateClientFor(_alice);
        using var bob = Factory.CreateClientFor(_bob);

        var media = await UploadAsync(alice, CreatePng(200, 100));
        var post = await CreatePostAsync(alice, "Maxfiy qoralama", "<p>Faqat Alice uchun</p>");
        var postId = post.GetProperty("id").GetGuid();

        (await bob.GetAsync($"/api/my/posts/{postId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.PostAsync($"/api/my/posts/{postId}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.DeleteAsync($"/api/my/posts/{postId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetAsync($"/api/my/media/{media.GetProperty("id").GetGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Bob Alice'ning media'sini o'z postiga qo'sha olmaydi.
        var foreign = await bob.PostAsJsonAsync("/api/my/posts", new
        {
            title = "O'g'irlik",
            content = new { format = "html", body = $"<img data-media-id=\"{media.GetProperty("id").GetGuid()}\" src=\"/x\">" }
        }, Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(foreign)).GetProperty("code").GetString().ShouldBe("Post.InvalidMediaReference");

        using var anonymous = Factory.CreateClientFor(null);
        (await anonymous.GetAsync("/api/my/posts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Full_text_search_finds_published_post_by_word_in_body()
    {
        postgres.SkipIfUnavailable();
        using var alice = Factory.CreateClientFor(_alice);
        using var anonymous = Factory.CreateClientFor(null);

        var post = await CreatePostAsync(alice, "Bahor", "<p>Bog'da qizg'aldoqlar ochildi.</p>");
        await CreatePostAsync(alice, "Qish", "<p>Qor yog'di.</p>");
        (await alice.PostAsync($"/api/my/posts/{post.GetProperty("id").GetGuid()}/publish", null, Ct)).EnsureSuccessStatusCode();

        var found = await ReadJsonAsync(await anonymous.GetAsync("/api/public/posts?q=ochildi", Ct));
        found.GetProperty("totalCount").GetInt32().ShouldBe(1);
        found.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Bahor");

        // Qoralama ("Qish") public qidiruvda chiqmaydi.
        var draft = await ReadJsonAsync(await anonymous.GetAsync("/api/public/posts?q=yog'di", Ct));
        draft.GetProperty("totalCount").GetInt32().ShouldBe(0);

        var byAuthor = await ReadJsonAsync(await anonymous.GetAsync("/api/public/posts?author=alice&sort=popular", Ct));
        byAuthor.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Stale_version_on_update_returns_conflict()
    {
        postgres.SkipIfUnavailable();
        using var alice = Factory.CreateClientFor(_alice);

        var post = await CreatePostAsync(alice, "Versiya", "<p>v1</p>");
        var postId = post.GetProperty("id").GetGuid();
        var version = post.GetProperty("version").GetUInt32();

        object Update(string body, uint v) => new
        {
            title = "Versiya",
            content = new { format = "html", body },
            version = v
        };

        var first = await alice.PutAsJsonAsync($"/api/my/posts/{postId}", Update("<p>v2</p>", version), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        (await ReadJsonAsync(first)).GetProperty("version").GetUInt32().ShouldNotBe(version);

        var stale = await alice.PutAsJsonAsync($"/api/my/posts/{postId}", Update("<p>v3</p>", version), Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var revisions = await ReadJsonAsync(await alice.GetAsync($"/api/my/posts/{postId}/revisions", Ct));
        revisions.GetArrayLength().ShouldBe(1);
    }
}
