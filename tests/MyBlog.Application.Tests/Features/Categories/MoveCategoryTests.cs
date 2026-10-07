using MyBlog.Application.Features.Categories.MoveCategory;
using MyBlog.Domain.Categories;
using static MyBlog.Application.Tests.Features.Categories.CategoryTestData;

namespace MyBlog.Application.Tests.Features.Categories;

public sealed class MoveCategoryTests
{
    [Fact]
    public void Moves_under_new_parent_at_position_and_renumbers_siblings()
    {
        var root = Create("root");
        var a = Create("a", root.Id, 0);
        var b = Create("b", root.Id, 1);
        var other = Create("other", order: 1);
        List<Category> all = [root, a, b, other];

        var result = MoveCategoryCommandHandler.Move(all, other.Id, root.Id, 1);

        result.IsSuccess.ShouldBeTrue();
        other.ParentId.ShouldBe(root.Id);
        (a.Order, other.Order, b.Order).ShouldBe((0, 1, 2));
    }

    [Fact]
    public void Old_siblings_are_renumbered_and_large_order_appends()
    {
        var x = Create("x", order: 0);
        var y = Create("y", order: 1);
        var z = Create("z", order: 2);
        var parent = Create("parent", order: 3);
        List<Category> all = [x, y, z, parent];

        MoveCategoryCommandHandler.Move(all, y.Id, parent.Id, 99).IsSuccess.ShouldBeTrue();

        y.ParentId.ShouldBe(parent.Id);
        y.Order.ShouldBe(0);
        (x.Order, z.Order, parent.Order).ShouldBe((0, 1, 2));
    }

    [Fact]
    public void Rejects_cycle_and_leaves_tree_unchanged()
    {
        var a = Create("a");
        var b = Create("b", a.Id);
        List<Category> all = [a, b];

        var result = MoveCategoryCommandHandler.Move(all, a.Id, b.Id, 0);

        result.Error.ShouldBe(CategoryErrors.CircularReference);
        a.ParentId.ShouldBeNull();
    }

    [Fact]
    public void Rejects_depth_overflow()
    {
        var a = Create("a");
        var b = Create("b", a.Id);
        var c = Create("c", b.Id);
        var d = Create("d");
        var e = Create("e", d.Id);
        List<Category> all = [a, b, c, d, e];

        MoveCategoryCommandHandler.Move(all, d.Id, b.Id, 0).Error.ShouldBe(CategoryErrors.MaxDepthExceeded);
    }
}
