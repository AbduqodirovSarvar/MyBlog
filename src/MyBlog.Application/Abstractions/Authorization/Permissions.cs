namespace MyBlog.Application.Abstractions.Authorization;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, User];
}

/// <summary>
/// Ruxsatlar. Rollarga RolePermission jadvali orqali biriktiriladi va JWT'ga "permission" claim sifatida yoziladi.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Profile { public const string Manage = "Profile.Manage"; }
    public static class Categories { public const string Manage = "Categories.Manage"; }
    public static class Tags { public const string Manage = "Tags.Manage"; }
    public static class Posts { public const string Manage = "Posts.Manage"; }
    public static class Media { public const string Manage = "Media.Manage"; }

    public static class Comments
    {
        public const string Write = "Comments.Write";
        public const string Moderate = "Comments.Moderate";
    }

    public static class Reactions { public const string Write = "Reactions.Write"; }

    public static class Users
    {
        public const string View = "Users.View";
        public const string Block = "Users.Block";
        public const string ManageRoles = "Users.ManageRoles";
    }

    public static readonly IReadOnlyList<string> All =
    [
        Profile.Manage, Categories.Manage, Tags.Manage, Posts.Manage, Media.Manage,
        Comments.Write, Comments.Moderate, Reactions.Write,
        Users.View, Users.Block, Users.ManageRoles
    ];

    /// <summary>Seed uchun: rol bo'yicha default ruxsatlar.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultsByRole =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Roles.SuperAdmin] = All,
            [Roles.Admin] =
            [
                Profile.Manage, Categories.Manage, Tags.Manage, Posts.Manage, Media.Manage,
                Comments.Write, Comments.Moderate, Reactions.Write, Users.View, Users.Block
            ],
            [Roles.User] =
            [
                Profile.Manage, Categories.Manage, Tags.Manage, Posts.Manage, Media.Manage,
                Comments.Write, Reactions.Write
            ]
        };
}
