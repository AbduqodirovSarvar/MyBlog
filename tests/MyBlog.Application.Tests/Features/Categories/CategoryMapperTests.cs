using MyBlog.Application.Features.Categories.Common;
using static MyBlog.Application.Tests.Features.Categories.CategoryTestData;

namespace MyBlog.Application.Tests.Features.Categories;

public sealed class CategoryMapperTests
{
    private static readonly Dictionary<Guid, int> NoCounts = [];
    private static readonly Dictionary<Guid, string> NoUrls = [];

    [Fact]
    public void Name_uses_requested_culture_when_translated()
    {
        var category = Create("tech", ru: "Технологии");

        var dto = CategoryMapper.ToDto(category, "ru", "uz", NoCounts, NoUrls);

        dto.Name.ShouldBe("Технологии");
        dto.Translations.Select(t => t.Culture).ShouldBe(["uz", "ru"]);
    }

    [Fact]
    public void Name_falls_back_to_default_culture()
    {
        var category = Create("tech", ru: "Технологии");

        CategoryMapper.ToDto(category, "en", "uz", NoCounts, NoUrls).Name.ShouldBe("tech uz");
        CategoryMapper.ToDto(category, "uz-Cyrl", "uz", NoCounts, NoUrls).Name.ShouldBe("tech uz");
    }

    [Fact]
    public void Tree_nests_children_and_carries_counts()
    {
        var root = Create("root");
        var child = Create("child", root.Id);
        var counts = new Dictionary<Guid, int> { [child.Id] = 3 };

        var tree = CategoryMapper.ToTree([child, root], "uz", "uz", counts, NoUrls);

        var node = tree.ShouldHaveSingleItem();
        node.Id.ShouldBe(root.Id);
        node.Children.ShouldHaveSingleItem().PostCount.ShouldBe(3);
    }

    [Fact]
    public void Public_tree_sums_totals_and_hides_children_of_missing_parents()
    {
        var root = Create("root");
        var child = Create("child", root.Id);
        var orphan = Create("orphan", Guid.NewGuid());
        var counts = new Dictionary<Guid, int> { [root.Id] = 1, [child.Id] = 2, [orphan.Id] = 5 };

        var tree = CategoryMapper.ToPublicTree([root, child, orphan], "ru", "uz", counts, NoUrls);

        var node = tree.ShouldHaveSingleItem();
        node.Name.ShouldBe("root uz");
        node.PostCount.ShouldBe(1);
        node.TotalPostCount.ShouldBe(3);
    }
}
