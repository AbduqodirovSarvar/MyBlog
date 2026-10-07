using MyBlog.Domain.Common;
using static MyBlog.Domain.Tags.TagConstraints;

namespace MyBlog.Domain.Tags;

/// <summary>Foydalanuvchining shaxsiy tegi. (OwnerId, Slug) unikal.</summary>
public sealed class Tag : AuditableEntity, IAggregateRoot, IOwnedEntity
{
    private Tag() { }

    private Tag(Guid ownerId, string name, string slug)
    {
        OwnerId = ownerId;
        Name = name;
        Slug = slug;
    }

    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    public static Result<Tag> Create(Guid ownerId, string name, string slug)
    {
        if (ownerId == Guid.Empty)
            return TagErrors.InvalidOwner;

        var validation = Validate(name, slug);
        return validation.IsFailure
            ? validation.Error
            : new Tag(ownerId, name.Trim(), slug.Trim());
    }

    public Result Rename(string name, string slug)
    {
        var validation = Validate(name, slug);
        if (validation.IsFailure)
            return validation;

        Name = name.Trim();
        Slug = slug.Trim();
        return Result.Success();
    }

    private static Result Validate(string? name, string? slug)
    {
        var nameValue = DomainRules.TrimToNull(name);
        if (nameValue is null)
            return TagErrors.NameRequired;
        if (nameValue.Length > NameMaxLength)
            return TagErrors.NameTooLong;

        var slugValue = slug?.Trim();
        if (slugValue is null || slugValue.Length > SlugMaxLength || !DomainRules.IsValidSlug(slugValue))
            return TagErrors.SlugInvalid;

        return Result.Success();
    }
}

public static class TagConstraints
{
    public const int NameMaxLength = 50;
    public const int SlugMaxLength = 60;
}

public static class TagErrors
{
    public static readonly Error NotFound = Error.NotFound("Tag.NotFound", "Tag was not found.");
    public static readonly Error InvalidOwner = Error.Validation("Tag.InvalidOwner", "Tag owner is required.");
    public static readonly Error NameRequired = Error.Validation("Tag.NameRequired", "Tag name is required.");

    public static readonly Error NameTooLong = Error.Validation("Tag.NameTooLong", "Tag name must not exceed {0} characters.")
        .WithArgs(NameMaxLength);

    public static readonly Error SlugInvalid = Error.Validation("Tag.SlugInvalid",
            "Slug must contain only lowercase latin letters, digits and hyphens and must not exceed {0} characters.")
        .WithArgs(SlugMaxLength);

    public static readonly Error SlugTaken = Error.Conflict("Tag.SlugTaken", "A tag with the same slug already exists.");
}
