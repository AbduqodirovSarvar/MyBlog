using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

/// <summary>Ko'nikma. Level ixtiyoriy (null), berilsa 1..100 oralig'ida bo'lishi kerak.</summary>
public sealed class Skill : Entity, IOrderedItem
{
    private Skill() { }

    private Skill(Guid profileId, string name, int? level, int order)
    {
        ProfileId = profileId;
        Name = name;
        Level = level;
        Order = order;
    }

    public Guid ProfileId { get; private set; }
    public string Name { get; private set; } = null!;
    public int? Level { get; private set; }
    public int Order { get; private set; }

    internal static Result<Skill> Create(Guid profileId, string name, int? level, int order)
    {
        var validation = Validate(name, level);
        return validation.IsFailure
            ? validation.Error
            : new Skill(profileId, name.Trim(), level, order);
    }

    internal Result Update(string name, int? level)
    {
        var validation = Validate(name, level);
        if (validation.IsFailure)
            return validation;

        Name = name.Trim();
        Level = level;
        return Result.Success();
    }

    void IOrderedItem.SetOrder(int order) => Order = order;

    public static bool IsValidLevel(int? level) => level is null or (>= SkillLevelMin and <= SkillLevelMax);

    private static Result Validate(string? name, int? level)
    {
        if (string.IsNullOrWhiteSpace(name))
            return UserProfileErrors.SkillNameRequired;
        if (name.Trim().Length > SkillNameMaxLength)
            return UserProfileErrors.SkillNameTooLong;
        if (!IsValidLevel(level))
            return UserProfileErrors.SkillLevelOutOfRange;

        return Result.Success();
    }
}
