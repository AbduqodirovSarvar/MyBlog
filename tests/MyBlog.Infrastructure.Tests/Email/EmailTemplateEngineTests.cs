using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.Email;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Email;

public sealed class EmailTemplateEngineTests
{
    private const string Template =
        "<title>Salom, {{name}}!</title>\n<p>Tasdiqlash uchun <a href=\"{{link}}\">bu yerni bosing</a>.</p>";

    private const string Layout = "<html lang=\"{{lang}}\"><head><title>{{title}}</title></head><body>{{body}}{{unknown}}</body></html>";

    [Fact]
    public void Extracts_subject_and_wraps_body_in_layout()
    {
        var values = new Dictionary<string, string> { ["name"] = "Ali", ["link"] = "https://myblog.uz/c?t=1&u=2" };

        var email = EmailTemplateEngine.Render(Template, Layout, values, "uz");

        email.Subject.ShouldBe("Salom, Ali!");
        email.HtmlBody.ShouldStartWith("<html lang=\"uz\"><head><title>Salom, Ali!</title></head><body><p>");
        email.HtmlBody.ShouldContain("href=\"https://myblog.uz/c?t=1&amp;u=2\"");
        email.HtmlBody.ShouldNotContain("{{");
        email.TextBody.ShouldBe("Tasdiqlash uchun bu yerni bosing (https://myblog.uz/c?t=1&u=2).");
    }

    [Fact]
    public void Html_encodes_values()
    {
        var values = new Dictionary<string, string> { ["name"] = "<script>alert(1)</script>", ["link"] = "x" };

        var email = EmailTemplateEngine.Render(Template, null, values, "uz");

        email.HtmlBody.ShouldNotContain("<script>");
        email.Subject.ShouldBe("Salom, <script>alert(1)</script>!");
    }

    [Fact]
    public void Body_placeholders_are_not_reprocessed_by_layout()
    {
        var values = new Dictionary<string, string> { ["name"] = "{{title}}", ["link"] = "x" };

        var email = EmailTemplateEngine.Render("<title>T</title><p>{{name}}</p>", Layout, values, "en");

        email.HtmlBody.ShouldContain("<p>{{title}}</p>");
    }

    [Fact]
    public async Task Renderer_throws_for_unknown_template_and_finds_layouts()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.NormalizeCulture(Arg.Any<string?>()).Returns("uz");
        localizer.DefaultCulture.Returns("uz");
        var renderer = new EmbeddedEmailTemplateRenderer(localizer);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            renderer.RenderAsync("does-not-exist", "uz", new Dictionary<string, string>(), TestContext.Current.CancellationToken));

        // _layout o'zi ham shablon sifatida render qilinishi mumkin (embedded resurslar yuklanganini tekshiradi).
        var layout = await renderer.RenderAsync(EmbeddedEmailTemplateRenderer.LayoutName, "uz",
            new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        layout.HtmlBody.ShouldContain("MyBlog");
    }
}
