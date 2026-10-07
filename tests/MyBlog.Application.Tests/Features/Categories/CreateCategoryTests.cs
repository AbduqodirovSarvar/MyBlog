using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Categories.CreateCategory;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Media;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Categories;

public sealed class CreateCategoryTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly IRepository<Category> _categories = Substitute.For<IRepository<Category>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeAuthorCache _cache = new();
    private readonly List<CategoryNode> _existing = [];

    private CreateCategoryCommandHandler CreateHandler()
    {
        _categories.ListAsync(Arg.Any<ISpecification<Category, CategoryNode>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _existing.ToList());

        var localizer = Substitute.For<ILocalizer>();
        localizer.CurrentCulture.Returns("ru");
        localizer.DefaultCulture.Returns("uz");

        return new CreateCategoryCommandHandler(_categories, Substitute.For<IReadRepository<MediaFile>>(), _unitOfWork,
            TestUsers.Authenticated(_userId), new SimpleSlugGenerator(), localizer, new FakeMediaUrlResolver(), _cache);
    }

    private static CreateCategoryCommand Command(string uzName, Guid? parentId = null, string? slug = null) =>
        new([new CategoryTranslationData("uz", uzName), new CategoryTranslationData("ru", "Новости")], parentId, slug);

    [Fact]
    public async Task Generated_slug_gets_numeric_suffix_when_taken()
    {
        _existing.Add(new CategoryNode(Guid.NewGuid(), null, 0, "news"));
        _existing.Add(new CategoryNode(Guid.NewGuid(), null, 1, "news-2"));
        var handler = CreateHandler();

        var result = await handler.Handle(Command("News"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Slug.ShouldBe("news-3");
        result.Value.Order.ShouldBe(2);
        result.Value.Name.ShouldBe("Новости");
        _categories.Received(1).Add(Arg.Is<Category>(c => c.Slug == "news-3" && c.OwnerId == _userId));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.CurrentUserInvalidations.ShouldBe(1);
    }

    [Fact]
    public async Task Explicit_taken_slug_is_a_conflict()
    {
        _existing.Add(new CategoryNode(Guid.NewGuid(), null, 0, "news"));
        var handler = CreateHandler();

        var result = await handler.Handle(Command("Anything", slug: "news"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CategoryErrors.SlugTaken);
        _categories.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Fact]
    public async Task Parent_must_exist_and_depth_is_limited()
    {
        var level1 = Guid.NewGuid();
        var level2 = Guid.NewGuid();
        var level3 = Guid.NewGuid();
        _existing.AddRange([new CategoryNode(level1, null, 0, "a"), new CategoryNode(level2, level1, 0, "b"),
            new CategoryNode(level3, level2, 0, "c")]);
        var handler = CreateHandler();
        var ct = TestContext.Current.CancellationToken;

        (await handler.Handle(Command("X", Guid.NewGuid()), ct)).Error.ShouldBe(CategoryErrors.ParentNotFound);
        (await handler.Handle(Command("X", level3), ct)).Error.ShouldBe(CategoryErrors.MaxDepthExceeded);

        var ok = await handler.Handle(Command("X", level2), ct);
        ok.IsSuccess.ShouldBeTrue();
        ok.Value.ParentId.ShouldBe(level2);
        ok.Value.Order.ShouldBe(1);
    }

    [Fact]
    public void Validator_requires_uzbek_translation_and_unique_cultures()
    {
        var validator = new CreateCategoryCommandValidator();

        validator.Validate(new CreateCategoryCommand([new CategoryTranslationData("ru", "Новости")]))
            .Errors.ShouldContain(e => e.ErrorCode == CategoryErrors.DefaultTranslationRequired.Code);

        validator.Validate(new CreateCategoryCommand([new CategoryTranslationData("uz", "A"), new CategoryTranslationData("UZ", "B")]))
            .Errors.ShouldContain(e => e.ErrorCode == "Category.DuplicateCulture");

        validator.Validate(new CreateCategoryCommand([new CategoryTranslationData("uz", "A")], Slug: "Bad Slug"))
            .Errors.ShouldContain(e => e.ErrorCode == "Category.SlugFormatInvalid");

        validator.Validate(new CreateCategoryCommand([new CategoryTranslationData("uz", "A")])).IsValid.ShouldBeTrue();
    }
}
