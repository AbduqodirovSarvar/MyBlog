using Microsoft.Extensions.Options;
using MyBlog.Infrastructure.Content;

namespace MyBlog.Infrastructure.Tests.Content;

public sealed class GanssHtmlSanitizerTests
{
    private readonly GanssHtmlSanitizer _sanitizer = new(Options.Create(new ContentOptions()));

    [Fact]
    public void Removes_script_tags()
    {
        var result = _sanitizer.Sanitize("<p>Salom</p><script>alert('x')</script>");

        result.ShouldBe("<p>Salom</p>");
    }

    [Fact]
    public void Removes_event_handler_attributes()
    {
        var result = _sanitizer.Sanitize("<img src=\"/media/a.webp\" onerror=\"alert(1)\">");

        result.ShouldNotContain("onerror");
        result.ShouldContain("src=\"/media/a.webp\"");
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>")]
    public void Removes_dangerous_hrefs(string html)
    {
        var result = _sanitizer.Sanitize(html);

        result.ShouldNotContain("javascript", Case.Insensitive);
        result.ShouldNotContain("data:");
    }

    [Fact]
    public void Keeps_allowed_links_including_relative_urls()
    {
        var result = _sanitizer.Sanitize(
            "<a href=\"https://example.com/a\">a</a><a href=\"mailto:me@example.com\">b</a><a href=\"/posts/salom\">c</a>");

        result.ShouldContain("href=\"https://example.com/a\"");
        result.ShouldContain("href=\"mailto:me@example.com\"");
        result.ShouldContain("href=\"/posts/salom\"");
    }

    [Fact]
    public void Keeps_editor_figure_with_media_attributes_and_allowed_styles()
    {
        const string html =
            "<figure class=\"image\" data-media-id=\"0199a1b2-c3d4-7e8f-9012-3456789abcde\" data-align=\"left\" " +
            "data-wrap=\"true\" data-width=\"50\" style=\"width: 50%; float: left; position: fixed; background-image: url(x)\">" +
            "<img src=\"/media/2026/10/a.webp\" alt=\"Rasm\" loading=\"lazy\" width=\"640\" height=\"480\">" +
            "<figcaption>Izoh</figcaption></figure>";

        var result = _sanitizer.Sanitize(html);

        result.ShouldContain("<figure");
        result.ShouldContain("<figcaption>Izoh</figcaption>");
        result.ShouldContain("data-media-id=\"0199a1b2-c3d4-7e8f-9012-3456789abcde\"");
        result.ShouldContain("data-align=\"left\"");
        result.ShouldContain("data-wrap=\"true\"");
        result.ShouldContain("data-width=\"50\"");
        result.ShouldContain("width: 50%");
        result.ShouldContain("float: left");
        result.ShouldNotContain("position");
        result.ShouldNotContain("background");
        result.ShouldContain("src=\"/media/2026/10/a.webp\"");
        result.ShouldContain("loading=\"lazy\"");
    }

    [Fact]
    public void Removes_unknown_data_attributes()
    {
        var result = _sanitizer.Sanitize("<p data-evil=\"1\" data-align=\"center\">x</p>");

        result.ShouldNotContain("data-evil");
        result.ShouldContain("data-align=\"center\"");
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://player.vimeo.com/video/76979871")]
    public void Keeps_whitelisted_iframes(string src)
    {
        var result = _sanitizer.Sanitize($"<iframe src=\"{src}\" width=\"560\" height=\"315\" frameborder=\"0\" allowfullscreen></iframe>");

        result.ShouldContain("<iframe");
        result.ShouldContain($"src=\"{src}\"");
        result.ShouldContain("allowfullscreen");
    }

    [Theory]
    [InlineData("https://evil.example.com/embed/x")]
    [InlineData("http://www.youtube.com/embed/x")]
    [InlineData("https://youtube.com.evil.com/embed/x")]
    [InlineData("/relative/frame")]
    [InlineData("javascript:alert(1)")]
    public void Removes_iframes_from_other_hosts(string src)
    {
        var result = _sanitizer.Sanitize($"<p>a</p><iframe src=\"{src}\"></iframe><p>b</p>");

        result.ShouldNotContain("<iframe");
        result.ShouldContain("<p>a</p>");
        result.ShouldContain("<p>b</p>");
    }

    [Fact]
    public void Removes_iframe_without_src()
    {
        _sanitizer.Sanitize("<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>").ShouldNotContain("<iframe");
    }

    [Fact]
    public void Rejects_html_longer_than_limit()
    {
        var sanitizer = new GanssHtmlSanitizer(Options.Create(new ContentOptions { MaxHtmlLength = 10 }));

        Should.Throw<ArgumentException>(() => sanitizer.Sanitize("<p>12345678901</p>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_returns_empty(string? html) => _sanitizer.Sanitize(html!).ShouldBeEmpty();
}
