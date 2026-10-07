using MyBlog.Domain.Users;

namespace MyBlog.Domain.Tests.Users;

public sealed class UserProfileTests
{
    private static UserProfile NewProfile() =>
        UserProfile.Create(Guid.CreateVersion7(), "sarvar", "Sarvar", "Abdukadirov").Value;

    [Fact]
    public void Create_SetsOwnerToIdAndDefaults()
    {
        var userId = Guid.CreateVersion7();

        var profile = UserProfile.Create(userId, "john_doe").Value;

        profile.Id.ShouldBe(userId);
        profile.OwnerId.ShouldBe(userId);
        profile.DisplayName.ShouldBe("john_doe");
        profile.PreferredCulture.ShouldBe("uz");
        profile.NotifyOnComment.ShouldBeTrue();
        profile.NotifyOnReply.ShouldBeTrue();
    }

    [Fact]
    public void Create_DisplayNameFromNames() =>
        NewProfile().DisplayName.ShouldBe("Sarvar Abdukadirov");

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("")]
    public void Create_InvalidUsername_Fails(string username) =>
        UserProfile.Create(Guid.CreateVersion7(), username).Error.ShouldBe(UserProfileErrors.UsernameInvalid);

    [Fact]
    public void Create_EmptyUserId_Fails() =>
        UserProfile.Create(Guid.Empty, "valid").Error.ShouldBe(UserProfileErrors.InvalidUserId);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void AddSkill_ValidLevel_Succeeds(int? level)
    {
        var profile = NewProfile();

        var result = profile.AddSkill("C#", level);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Level.ShouldBe(level);
        result.Value.ProfileId.ShouldBe(profile.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public void AddSkill_LevelOutOfRange_Fails(int level)
    {
        var profile = NewProfile();

        profile.AddSkill("C#", level).Error.ShouldBe(UserProfileErrors.SkillLevelOutOfRange);
        profile.Skills.ShouldBeEmpty();
    }

    [Fact]
    public void AddSkill_Duplicate_Fails()
    {
        var profile = NewProfile();
        profile.AddSkill("C#", null);

        profile.AddSkill("c#", 10).Error.ShouldBe(UserProfileErrors.SkillDuplicate);
    }

    [Fact]
    public void UpdateSkill_InvalidLevel_KeepsOldValues()
    {
        var profile = NewProfile();
        var skill = profile.AddSkill("C#", 80).Value;

        profile.UpdateSkill(skill.Id, "C#", 0).Error.ShouldBe(UserProfileErrors.SkillLevelOutOfRange);

        skill.Level.ShouldBe(80);
    }

    [Fact]
    public void ReorderSkills_SetsOrderByPosition()
    {
        var profile = NewProfile();
        var a = profile.AddSkill("A", null).Value;
        var b = profile.AddSkill("B", null).Value;
        var c = profile.AddSkill("C", null).Value;

        profile.ReorderSkills([c.Id, a.Id, b.Id]).IsSuccess.ShouldBeTrue();

        c.Order.ShouldBe(0);
        a.Order.ShouldBe(1);
        b.Order.ShouldBe(2);
    }

    [Fact]
    public void ReorderSkills_Incomplete_Fails()
    {
        var profile = NewProfile();
        var a = profile.AddSkill("A", null).Value;
        profile.AddSkill("B", null);

        profile.ReorderSkills([a.Id]).Error.ShouldBe(UserProfileErrors.ReorderMismatch);
    }

    [Fact]
    public void RemoveSocialLink_RenumbersRemaining()
    {
        var profile = NewProfile();
        var first = profile.AddSocialLink(SocialPlatform.GitHub, "https://github.com/x").Value;
        var second = profile.AddSocialLink(SocialPlatform.Telegram, "https://t.me/x").Value;

        profile.RemoveSocialLink(first.Id).IsSuccess.ShouldBeTrue();

        profile.SocialLinks.ShouldHaveSingleItem().ShouldBeSameAs(second);
        second.Order.ShouldBe(0);
    }

    [Fact]
    public void AddSocialLink_InvalidUrl_Fails() =>
        NewProfile().AddSocialLink(SocialPlatform.Website, "javascript:alert(1)")
            .Error.ShouldBe(UserProfileErrors.SocialLinkUrlInvalid);

    [Fact]
    public void AddExperience_CurrentWithEndDate_Fails() =>
        NewProfile().AddExperience("Acme", "Dev", null, new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1), true, null)
            .Error.ShouldBe(UserProfileErrors.CurrentWithEndDate);

    [Fact]
    public void AddEducation_EndBeforeStart_Fails() =>
        NewProfile().AddEducation("TATU", null, null, new DateOnly(2020, 1, 1), new DateOnly(2019, 1, 1), null)
            .Error.ShouldBe(UserProfileErrors.EndDateBeforeStartDate);

    [Fact]
    public void UpdatePreferences_UnsupportedCulture_Fails() =>
        NewProfile().UpdatePreferences("de", true, true).Error.Code.ShouldBe(UserProfileErrors.CultureNotSupported.Code);

    [Fact]
    public void UpdateContactInfo_InvalidEmail_Fails() =>
        NewProfile().UpdateContactInfo(null, "not-an-email", null).Error.ShouldBe(UserProfileErrors.PublicEmailInvalid);
}
