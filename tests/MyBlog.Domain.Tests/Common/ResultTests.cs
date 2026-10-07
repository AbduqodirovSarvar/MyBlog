using MyBlog.Domain.Common;

namespace MyBlog.Domain.Tests.Common;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.Validation("Sample.Invalid", "Sample is invalid.");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_CarriesError()
    {
        Result result = SampleError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Failure_WithNoneError_Throws() =>
        Should.Throw<InvalidOperationException>(() => Result.Failure(Error.None));

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        Result<int> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void WithArgs_KeepsCodeAndAddsArgs()
    {
        var error = SampleError.WithArgs(5, "x");

        error.Code.ShouldBe(SampleError.Code);
        error.Args.ShouldBe(new object[] { 5, "x" });
        SampleError.Args.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("uz", "uz")]
    [InlineData("UZ-cyrl", "uz-Cyrl")]
    [InlineData(" ru ", "ru")]
    [InlineData("de", null)]
    [InlineData("", null)]
    public void Cultures_Normalize(string input, string? expected) =>
        Cultures.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("hello-world", true)]
    [InlineData("post1", true)]
    [InlineData("Hello", false)]
    [InlineData("a--b", false)]
    [InlineData("-a", false)]
    [InlineData("", false)]
    public void DomainRules_IsValidSlug(string slug, bool expected) =>
        DomainRules.IsValidSlug(slug).ShouldBe(expected);
}
