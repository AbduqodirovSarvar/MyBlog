using MyBlog.Infrastructure.Text;

namespace MyBlog.Infrastructure.Tests.Text;

public sealed class SlugGeneratorTests
{
    private readonly SlugGenerator _generator = new();

    [Theory]
    [InlineData("O'zbekiston g'alabasi", "ozbekiston-galabasi")]
    [InlineData("Oʻzbekiston Respublikasi", "ozbekiston-respublikasi")]
    [InlineData("Ma’rifat va sun‘iy intellekt", "marifat-va-suniy-intellekt")]
    [InlineData("Gʼijduvon `shahri`", "gijduvon-shahri")]
    public void Uzbek_latin_apostrophes_are_removed(string input, string expected) =>
        _generator.Generate(input).ShouldBe(expected);

    [Theory]
    [InlineData("Ўзбекистон ҳақида қисқача", "ozbekiston-haqida-qisqacha")]
    [InlineData("Ғалаба ва Жасорат", "galaba-va-jasorat")]
    [InlineData("Янги хабарлар: юксак чўққи", "yangi-xabarlar-yuksak-choqqi")]
    [InlineData("Маърифат", "marifat")]
    public void Uzbek_cyrillic_is_transliterated(string input, string expected) =>
        _generator.Generate(input).ShouldBe(expected);

    [Theory]
    [InlineData("Съешь ещё этих мягких булок", "sesh-eshyo-etikh-myagkikh-bulok")]
    [InlineData("Новые жилые дома", "novye-zhilye-doma")]
    [InlineData("Привет, мир!", "privet-mir")]
    [InlineData("Цирк и щука", "tsirk-i-shuka")]
    public void Russian_is_transliterated(string input, string expected) =>
        _generator.Generate(input).ShouldBe(expected);

    [Theory]
    [InlineData("C# & .NET 10!", "c-net-10")]
    [InlineData("  Hello   World  ", "hello-world")]
    [InlineData("Café déjà vu", "cafe-deja-vu")]
    [InlineData("--already--slugged--", "already-slugged")]
    [InlineData("2026 yil: top-10", "2026-yil-top-10")]
    public void Normalizes_case_symbols_and_dashes(string input, string expected) =>
        _generator.Generate(input).ShouldBe(expected);

    [Theory]
    [InlineData("alpha beta gamma", 12, "alpha-beta")]
    [InlineData("alpha beta gamma", 10, "alpha-beta")]
    [InlineData("alpha beta gamma", 11, "alpha-beta")]
    [InlineData("abcdefghijkl", 5, "abcde")]
    public void Truncates_on_dash_boundary(string input, int maxLength, string expected)
    {
        var slug = _generator.Generate(input, maxLength);

        slug.ShouldBe(expected);
        slug.Length.ShouldBeLessThanOrEqualTo(maxLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ??? ---")]
    [InlineData("'''")]
    public void Empty_result_falls_back_to_random_token(string input)
    {
        var slug = _generator.Generate(input);

        slug.ShouldMatch("^[0-9a-f]{8}$");
        _generator.Generate(input).ShouldNotBe(slug);
    }
}
