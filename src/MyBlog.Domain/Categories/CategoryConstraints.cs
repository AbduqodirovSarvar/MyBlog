using MyBlog.Domain.Common;

namespace MyBlog.Domain.Categories;

public static class CategoryConstraints
{
    public const int SlugMaxLength = 120;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int CultureMaxLength = Cultures.MaxLength;

    /// <summary>Daraxtning maksimal chuqurligi (ildiz = 1-daraja). Application qatlamida repository orqali tekshiriladi.</summary>
    public const int MaxDepth = 3;
}
