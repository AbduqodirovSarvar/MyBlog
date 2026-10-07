using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

public enum SocialPlatform
{
    GitHub = 1,
    Telegram = 2,
    LinkedIn = 3,
    X = 4,
    Instagram = 5,
    Facebook = 6,
    YouTube = 7,
    Website = 8,
    Other = 99
}

/// <summary>Tartib raqami bor child elementlar (Reorder uchun).</summary>
internal interface IOrderedItem
{
    Guid Id { get; }
    int Order { get; }
    void SetOrder(int order);
}

/// <summary>Profildagi ijtimoiy tarmoq havolasi. Faqat <see cref="UserProfile"/> orqali boshqariladi.</summary>
public sealed class SocialLink : Entity, IOrderedItem
{
    private SocialLink() { }

    private SocialLink(Guid profileId, SocialPlatform platform, string url, int order)
    {
        ProfileId = profileId;
        Platform = platform;
        Url = url;
        Order = order;
    }

    public Guid ProfileId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public string Url { get; private set; } = null!;
    public int Order { get; private set; }

    internal static Result<SocialLink> Create(Guid profileId, SocialPlatform platform, string url, int order)
    {
        var validation = Validate(platform, url);
        return validation.IsFailure
            ? validation.Error
            : new SocialLink(profileId, platform, url.Trim(), order);
    }

    internal Result Update(SocialPlatform platform, string url)
    {
        var validation = Validate(platform, url);
        if (validation.IsFailure)
            return validation;

        Platform = platform;
        Url = url.Trim();
        return Result.Success();
    }

    void IOrderedItem.SetOrder(int order) => Order = order;

    private static Result Validate(SocialPlatform platform, string? url)
    {
        if (!Enum.IsDefined(platform))
            return UserProfileErrors.SocialPlatformInvalid;

        var trimmed = url?.Trim();
        if (trimmed is null || trimmed.Length > UrlMaxLength || !DomainRules.IsValidHttpUrl(trimmed))
            return UserProfileErrors.SocialLinkUrlInvalid;

        return Result.Success();
    }
}
