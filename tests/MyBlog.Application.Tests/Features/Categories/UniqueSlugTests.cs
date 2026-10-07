using MyBlog.Application.Features.Categories.Common;

namespace MyBlog.Application.Tests.Features.Categories;

public sealed class UniqueSlugTests
{
    [Fact]
    public void Returns_base_slug_when_free() =>
        UniqueSlug.Resolve("news", ["other"], 120).ShouldBe("news");

    [Fact]
    public void Appends_first_free_numeric_suffix()
    {
        UniqueSlug.Resolve("news", ["news"], 120).ShouldBe("news-2");
        UniqueSlug.Resolve("news", ["news", "news-2", "news-3"], 120).ShouldBe("news-4");
        UniqueSlug.Resolve("news", ["news", "news-3"], 120).ShouldBe("news-2");
    }

    [Fact]
    public void Truncates_base_to_fit_max_length()
    {
        var slug = UniqueSlug.Resolve("abcdefghij", ["abcdefghij"], 10);

        slug.ShouldBe("abcdefgh-2");
        slug.Length.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void Truncation_does_not_leave_double_hyphen() =>
        UniqueSlug.Resolve("abcdefg-ij", ["abcdefg-ij"], 10).ShouldBe("abcdefg-2");
}
