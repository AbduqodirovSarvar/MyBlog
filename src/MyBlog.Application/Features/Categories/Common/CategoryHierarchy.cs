using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Categories.Common;

/// <summary>
/// Foydalanuvchi kategoriyalari daraxti ustida tekshiruvlar: chuqurlik, sikl, ko'chirish.
/// Ildiz kategoriya 1-darajada.
/// </summary>
internal sealed class CategoryHierarchy
{
    private readonly Dictionary<Guid, Guid?> _parents;
    private readonly ILookup<Guid?, Guid> _children;

    public CategoryHierarchy(IEnumerable<(Guid Id, Guid? ParentId)> nodes)
    {
        _parents = nodes.ToDictionary(n => n.Id, n => n.ParentId);
        _children = _parents.ToLookup(p => p.Value, p => p.Key);
    }

    public bool Contains(Guid id) => _parents.ContainsKey(id);

    /// <summary>Ildiz = 1. Buzilgan (siklik) ma'lumotda cheksiz aylanmaslik uchun himoya bor.</summary>
    public int Depth(Guid id)
    {
        var depth = 0;
        Guid? current = id;
        var visited = new HashSet<Guid>();

        while (current is { } node && visited.Add(node) && _parents.TryGetValue(node, out var parent))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    /// <summary>Kategoriya va uning barcha avlodlari egallaydigan darajalar soni (bargi = 1).</summary>
    public int SubtreeHeight(Guid id) => SubtreeHeight(id, []);

    private int SubtreeHeight(Guid id, HashSet<Guid> visited)
    {
        if (!visited.Add(id))
            return 0;

        var max = 0;
        foreach (var child in _children[id])
            max = Math.Max(max, SubtreeHeight(child, visited));

        return max + 1;
    }

    /// <summary><paramref name="candidate"/> — <paramref name="ancestor"/>'ning avlodimi (o'zi emas).</summary>
    public bool IsDescendantOf(Guid candidate, Guid ancestor)
    {
        var visited = new HashSet<Guid>();
        var current = _parents.GetValueOrDefault(candidate);

        while (current is { } node && visited.Add(node))
        {
            if (node == ancestor)
                return true;
            current = _parents.GetValueOrDefault(node);
        }

        return false;
    }

    /// <summary>Yangi kategoriyani <paramref name="parentId"/> ostiga qo'shish mumkinmi.</summary>
    public Result CanAddChild(Guid? parentId)
    {
        if (parentId is not { } parent)
            return Result.Success();
        if (!Contains(parent))
            return CategoryErrors.ParentNotFound;

        return Depth(parent) + 1 > CategoryConstraints.MaxDepth
            ? CategoryErrors.MaxDepthExceeded
            : Result.Success();
    }

    /// <summary>
    /// Ko'chirish tekshiruvi: ota mavjud, o'zi yoki avlodi emas, va ko'chirilgan butun shox
    /// <see cref="CategoryConstraints.MaxDepth"/>'dan chuqurlashmaydi.
    /// </summary>
    public Result CanMove(Guid categoryId, Guid? newParentId)
    {
        if (!Contains(categoryId))
            return CategoryErrors.NotFound;

        var parentDepth = 0;
        if (newParentId is { } parent)
        {
            if (parent == categoryId)
                return CategoryErrors.CannotBeOwnParent;
            if (!Contains(parent))
                return CategoryErrors.ParentNotFound;
            if (IsDescendantOf(parent, categoryId))
                return CategoryErrors.CircularReference;

            parentDepth = Depth(parent);
        }

        return parentDepth + SubtreeHeight(categoryId) > CategoryConstraints.MaxDepth
            ? CategoryErrors.MaxDepthExceeded
            : Result.Success();
    }
}
