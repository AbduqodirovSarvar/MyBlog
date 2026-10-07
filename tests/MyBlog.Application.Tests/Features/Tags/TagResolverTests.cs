using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Tags;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Application.Features.Tags.Common;
using MyBlog.Domain.Tags;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Tags;

public sealed class TagResolverTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly IRepository<Tag> _tags = Substitute.For<IRepository<Tag>>();
    private readonly List<TagRef> _existing = [];

    private TagResolver CreateResolver()
    {
        _tags.ListAsync(Arg.Any<ISpecification<Tag, TagRef>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _existing.ToList());
        return new TagResolver(_tags, new SimpleSlugGenerator());
    }

    [Fact]
    public async Task Trims_skips_empty_and_dedupes_by_slug_keeping_input_order()
    {
        var resolver = CreateResolver();

        var ids = await resolver.ResolveAsync(_owner, ["  Dotnet ", "", "   ", "dotnet", "EF Core", "ef-core"],
            TestContext.Current.CancellationToken);

        ids.Count.ShouldBe(2);
        _tags.Received(2).Add(Arg.Any<Tag>());
        _tags.Received(1).Add(Arg.Is<Tag>(t => t.Name == "Dotnet" && t.Slug == "dotnet" && t.OwnerId == _owner));
        _tags.Received(1).Add(Arg.Is<Tag>(t => t.Name == "EF Core" && t.Slug == "ef-core"));
    }

    [Fact]
    public async Task Reuses_existing_tags_and_creates_missing()
    {
        var existingId = Guid.NewGuid();
        _existing.Add(new TagRef(existingId, "csharp"));
        var resolver = CreateResolver();

        var ids = await resolver.ResolveAsync(_owner, ["CSharp", "Linq"], TestContext.Current.CancellationToken);

        ids[0].ShouldBe(existingId);
        ids[1].ShouldNotBe(existingId);
        _tags.Received(1).Add(Arg.Is<Tag>(t => t.Slug == "linq" && t.Id == ids[1]));
    }

    [Fact]
    public async Task Takes_at_most_max_tags()
    {
        var resolver = CreateResolver();
        var names = Enumerable.Range(1, 15).Select(i => $"tag {i}").ToList();

        var ids = await resolver.ResolveAsync(_owner, names, TestContext.Current.CancellationToken);

        ids.Count.ShouldBe(ITagResolver.MaxTags);
        ids.Distinct().Count().ShouldBe(ITagResolver.MaxTags);
    }

    [Fact]
    public async Task Long_names_are_truncated_and_empty_input_returns_nothing()
    {
        var resolver = CreateResolver();
        var ct = TestContext.Current.CancellationToken;

        (await resolver.ResolveAsync(_owner, [], ct)).ShouldBeEmpty();
        await _tags.DidNotReceive().ListAsync(Arg.Any<ISpecification<Tag, TagRef>>(), Arg.Any<CancellationToken>());

        await resolver.ResolveAsync(_owner, [new string('x', TagConstraints.NameMaxLength + 20)], ct);
        _tags.Received(1).Add(Arg.Is<Tag>(t => t.Name.Length == TagConstraints.NameMaxLength));
    }
}
