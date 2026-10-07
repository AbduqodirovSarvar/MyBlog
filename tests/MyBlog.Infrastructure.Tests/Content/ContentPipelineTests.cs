using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Infrastructure.Content;
using MyBlog.Infrastructure.Content.Processing;
using MyBlog.Infrastructure.Text;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Content;

public sealed class ContentPipelineTests
{
    private readonly Guid _owner = Guid.CreateVersion7();
    private readonly Guid _stranger = Guid.CreateVersion7();
    private readonly List<MediaFile> _media = [];
    private readonly ContentPipelineOptions _pipelineOptions = new();
    private readonly ContentOptions _contentOptions = new();
    private readonly HtmlContentPipeline _pipeline;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ContentPipelineTests()
    {
        var repository = Substitute.For<IReadRepository<MediaFile>>();
        repository.ListAsync(Arg.Any<ISpecification<MediaFile>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var spec = ci.Arg<ISpecification<MediaFile>>();
                return _media.Where(m => spec.Criteria.All(c => c.Compile()(m))).ToList();
            });

        var storage = Substitute.For<IFileStorage>();
        storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>());

        _pipeline = new HtmlContentPipeline(new GanssHtmlSanitizer(Options.Create(_contentOptions)), repository, storage,
            new SlugGenerator(), Options.Create(_contentOptions), Options.Create(_pipelineOptions));
    }

    private MediaFile AddMedia(Guid owner, int width = 2000, int height = 1000, string? alt = null)
    {
        var key = $"2026/10/{Guid.NewGuid():N}";
        var variants = new[] { ("thumb", 320), ("medium", 960), ("large", 1920) }
            .Select(v => MediaVariant.Create(v.Item1, $"{key}_{v.Item1}.webp", v.Item2, v.Item2 / 2, 100, "image/webp").Value);
        var media = MediaFile.Create(owner, "a.png", "image/png", 1000, width, height, key + ".png", variants).Value;
        media.UpdateMetadata(alt, null);
        _media.Add(media);
        return media;
    }

    private Task<MyBlog.Domain.Common.Result<ProcessedContent>> Html(string html) =>
        new HtmlContentProcessor(_pipeline).ProcessAsync(new ContentInput("html", html), _owner, Ct);

    [Fact]
    public async Task Removes_xss_vectors()
    {
        var result = await Html(
            "<p onclick=\"alert(1)\">Salom</p><script>alert(1)</script><img src=\"x\" onerror=\"alert(1)\"><a href=\"javascript:alert(1)\">x</a>");

        result.IsSuccess.ShouldBeTrue();
        var html = result.Value.Html;
        html.ShouldNotContain("<script");
        html.ShouldNotContain("onclick");
        html.ShouldNotContain("onerror");
        html.ShouldNotContain("javascript:");
        html.ShouldContain("Salom");
    }

    [Fact]
    public async Task Preserves_word_like_figure_layout_and_rewrites_media_src()
    {
        var media = AddMedia(_owner, alt: "Tog' manzarasi");
        var html =
            $"<figure class=\"image image-style-align-left\" style=\"width: 40%; float: left; margin-right: 16px\" data-align=\"left\">" +
            $"<img src=\"https://evil.example.com/tracker.png\" data-media-id=\"{media.Id}\" srcset=\"https://evil.example.com/x 1w\">" +
            "<figcaption>Izoh</figcaption></figure><p>Matn rasm atrofida oqadi.</p>";

        var result = await Html(html);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        var output = result.Value.Html;
        output.ShouldContain("float: left");
        output.ShouldContain("width: 40%");
        output.ShouldContain("image-style-align-left");
        output.ShouldContain("<figcaption>Izoh</figcaption>");
        output.ShouldNotContain("evil.example.com");
        output.ShouldContain($"src=\"/media/{media.StorageKey}\"");
        output.ShouldContain($"/media/{media.GetVariant("thumb")!.StorageKey} 320w");
        output.ShouldContain($"/media/{media.StorageKey} 2000w");
        output.ShouldContain("loading=\"lazy\"");
        output.ShouldContain("alt=\"Tog' manzarasi\"");
        result.Value.MediaIds.ShouldBe([media.Id]);
    }

    [Fact]
    public async Task Media_id_on_figure_rewrites_inner_img_and_ids_are_deduplicated()
    {
        var media = AddMedia(_owner);
        var html = $"<figure data-media-id=\"{media.Id}\"><img src=\"/old.png\"></figure><p><img data-media-id=\"{media.Id}\" src=\"/x\"></p>";

        var result = await Html(html);

        result.Value.MediaIds.ShouldBe([media.Id]);
        result.Value.Html.ShouldNotContain("/old.png");
        result.Value.Html.ShouldNotContain("src=\"/x\"");
    }

    [Fact]
    public async Task Foreign_or_unknown_media_is_rejected()
    {
        var foreign = AddMedia(_stranger);

        (await Html($"<img data-media-id=\"{foreign.Id}\" src=\"/a\">")).Error.ShouldBe(PostErrors.InvalidMediaReference);
        (await Html($"<img data-media-id=\"{Guid.NewGuid()}\" src=\"/a\">")).Error.ShouldBe(PostErrors.InvalidMediaReference);
        (await Html("<img data-media-id=\"not-a-guid\" src=\"/a\">")).Error.ShouldBe(PostErrors.InvalidMediaReference);
    }

    [Fact]
    public async Task Headings_get_unique_ids_and_toc()
    {
        var result = await Html("<h2>Kirish</h2><p>a</p><h3>Tafsilot</h3><h2>Kirish</h2><h2 id=\"evil\">  </h2><h4>Kichik</h4>");

        var toc = result.Value.Toc;
        toc.Select(t => (t.Level, t.Id, t.Text)).ShouldBe([(2, "kirish", "Kirish"), (3, "tafsilot", "Tafsilot"), (2, "kirish-2", "Kirish")]);
        result.Value.Html.ShouldContain("<h2 id=\"kirish\">");
        result.Value.Html.ShouldContain("<h2 id=\"kirish-2\">");
        result.Value.Html.ShouldNotContain("evil");
    }

    [Fact]
    public async Task Plain_text_is_normalized_and_reading_time_is_computed()
    {
        var words = string.Join(' ', Enumerable.Repeat("so'z", 401));
        var result = await Html($"<h2>Bir</h2><p>ikki</p><p>uch<br>to'rt</p><ul><li>besh</li></ul><p>{words}</p>");

        result.Value.PlainText.ShouldStartWith("Bir ikki uch to'rt besh so'z");
        result.Value.PlainText.ShouldNotContain("  ");
        result.Value.ReadingTimeMinutes.ShouldBe(3); // 406 so'z / 200 = 2.03 → 3

        (await Html("<p>qisqa</p>")).Value.ReadingTimeMinutes.ShouldBe(1);
        (await Html("")).Value.ReadingTimeMinutes.ShouldBe(1);
    }

    [Fact]
    public async Task Too_long_html_is_rejected_before_sanitizing()
    {
        _contentOptions.MaxHtmlLength = 20;

        (await Html("<p>" + new string('a', 50) + "</p>")).Error.ShouldBe(PostErrors.ContentTooLong);
    }

    [Fact]
    public async Task Raw_document_processor_validates_json_and_stores_it_verbatim()
    {
        var processor = new HtmlWithRawDocumentContentProcessor(_pipeline, Options.Create(_pipelineOptions));
        const string raw = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"<script>\"}]}]}";

        var ok = await processor.ProcessAsync(new ContentInput("TipTap-JSON", "<p>Salom</p>", raw), _owner, Ct);
        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Format.ShouldBe("tiptap-json");
        ok.Value.Raw.ShouldBe(raw);

        var broken = await processor.ProcessAsync(new ContentInput("tiptap-json", "<p>a</p>", "{\"type\":"), _owner, Ct);
        broken.Error.ShouldBe(PostErrors.InvalidRawDocument);

        _pipelineOptions.MaxRawLength = 10;
        var tooLong = await processor.ProcessAsync(new ContentInput("tiptap-json", "<p>a</p>", raw), _owner, Ct);
        tooLong.Error.ShouldBe(PostErrors.ContentTooLong);
    }

    [Fact]
    public async Task Html_processor_ignores_raw()
    {
        var result = await new HtmlContentProcessor(_pipeline).ProcessAsync(new ContentInput("html", "<p>a</p>", "{}"), _owner, Ct);

        result.Value.Raw.ShouldBeNull();
    }

    [Fact]
    public void Factory_resolves_strategies_by_format()
    {
        var factory = new ContentProcessorFactory(
        [
            new HtmlContentProcessor(_pipeline),
            new HtmlWithRawDocumentContentProcessor(_pipeline, Options.Create(_pipelineOptions))
        ]);

        factory.Get("html").Value.ShouldBeOfType<HtmlContentProcessor>();
        factory.Get("TIPTAP-JSON").Value.ShouldBeOfType<HtmlWithRawDocumentContentProcessor>();
        factory.Get("quill-delta").Value.ShouldBeOfType<HtmlWithRawDocumentContentProcessor>();

        var unknown = factory.Get("markdown");
        unknown.Error.Code.ShouldBe("Post.UnsupportedContentFormat");
        unknown.Error.Args.ShouldBe(["markdown"]);
    }
}
