using MyBlog.Application.Features.Categories.Common;
using MyBlog.Domain.Categories;

namespace MyBlog.Application.Tests.Features.Categories;

public sealed class CategoryHierarchyTests
{
    // Daraxt:  A ─ B ─ C        D ─ E
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid();

    private static CategoryHierarchy Tree() => new([(A, null), (B, A), (C, B), (D, null), (E, D)]);

    [Fact]
    public void Depth_and_subtree_height_are_computed_from_root()
    {
        var tree = Tree();

        tree.Depth(A).ShouldBe(1);
        tree.Depth(C).ShouldBe(3);
        tree.SubtreeHeight(A).ShouldBe(3);
        tree.SubtreeHeight(D).ShouldBe(2);
        tree.SubtreeHeight(C).ShouldBe(1);
    }

    [Fact]
    public void Cannot_add_child_below_max_depth()
    {
        var tree = Tree();

        tree.CanAddChild(B).IsSuccess.ShouldBeTrue();
        tree.CanAddChild(null).IsSuccess.ShouldBeTrue();
        tree.CanAddChild(C).Error.ShouldBe(CategoryErrors.MaxDepthExceeded);
        tree.CanAddChild(Guid.NewGuid()).Error.ShouldBe(CategoryErrors.ParentNotFound);
    }

    [Fact]
    public void Move_under_own_descendant_is_a_cycle()
    {
        var tree = Tree();

        tree.CanMove(A, C).Error.ShouldBe(CategoryErrors.CircularReference);
        tree.CanMove(A, B).Error.ShouldBe(CategoryErrors.CircularReference);
        tree.CanMove(A, A).Error.ShouldBe(CategoryErrors.CannotBeOwnParent);
    }

    [Fact]
    public void Move_checks_depth_including_moved_subtree_height()
    {
        var tree = Tree();

        // D(2 daraja) → B ostiga: 2 + 2 = 4 > 3
        tree.CanMove(D, B).Error.ShouldBe(CategoryErrors.MaxDepthExceeded);
        // D → A ostiga: 1 + 2 = 3
        tree.CanMove(D, A).IsSuccess.ShouldBeTrue();
        // E (barg) → B ostiga: 2 + 1 = 3
        tree.CanMove(E, B).IsSuccess.ShouldBeTrue();
        // E → C ostiga: 3 + 1 = 4
        tree.CanMove(E, C).Error.ShouldBe(CategoryErrors.MaxDepthExceeded);
    }

    [Fact]
    public void Move_to_root_and_unknown_ids()
    {
        var tree = Tree();

        tree.CanMove(C, null).IsSuccess.ShouldBeTrue();
        tree.CanMove(C, Guid.NewGuid()).Error.ShouldBe(CategoryErrors.ParentNotFound);
        tree.CanMove(Guid.NewGuid(), A).Error.ShouldBe(CategoryErrors.NotFound);
    }

    [Fact]
    public void Corrupted_cyclic_data_does_not_loop_forever()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var tree = new CategoryHierarchy([(x, y), (y, x)]);

        tree.Depth(x).ShouldBe(2);
        tree.IsDescendantOf(x, y).ShouldBeTrue();
        tree.SubtreeHeight(x).ShouldBePositive();
    }
}
