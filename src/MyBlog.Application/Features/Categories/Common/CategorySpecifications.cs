using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Categories.Common;

/// <summary>Joriy foydalanuvchining barcha kategoriyalari (tarjimalar avtomatik yuklanadi).</summary>
internal sealed class OwnCategoriesSpec : Specification<Category>
{
    public OwnCategoriesSpec(bool readOnly)
    {
        OrderByAsc(c => c.Order);
        if (readOnly)
            ReadOnly();
    }
}

internal sealed class CategoryByIdSpec : Specification<Category>
{
    public CategoryByIdSpec(Guid id, bool readOnly = false)
    {
        Where(c => c.Id == id);
        if (readOnly)
            ReadOnly();
    }
}

internal sealed record CategoryNode(Guid Id, Guid? ParentId, int Order, string Slug);

/// <summary>Daraxt tekshiruvlari uchun yengil proyeksiya (joriy foydalanuvchi).</summary>
internal sealed class OwnCategoryNodesSpec : Specification<Category, CategoryNode>
{
    public OwnCategoryNodesSpec()
    {
        Select(c => new CategoryNode(c.Id, c.ParentId, c.Order, c.Slug));
        ReadOnly();
    }
}

internal sealed class CategoryChildrenSpec : Specification<Category>
{
    public CategoryChildrenSpec(Guid parentId) => Where(c => c.ParentId == parentId);
}

/// <summary>Muallifning faol kategoriyalari (ommaviy).</summary>
internal sealed class PublicCategoriesSpec : Specification<Category>
{
    public PublicCategoriesSpec(Guid ownerId)
    {
        IgnoreOwnership();
        Where(c => c.OwnerId == ownerId && c.IsActive);
        OrderByAsc(c => c.Order);
        ReadOnly();
    }
}

/// <summary>Kategoriyaga biriktirilgan (o'chirilmagan) postlar.</summary>
internal sealed class PostsInCategorySpec : Specification<Post>
{
    public PostsInCategorySpec(Guid categoryId) => Where(p => p.CategoryId == categoryId);
}
