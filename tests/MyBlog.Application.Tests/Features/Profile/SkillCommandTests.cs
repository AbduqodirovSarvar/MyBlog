using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Profile.Skills;
using MyBlog.Application.Features.Profile.UpdateAboutMe;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Features.Profile;

public sealed class SkillCommandTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly UserProfile _profile;
    private readonly IRepository<UserProfile> _profiles = Substitute.For<IRepository<UserProfile>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeAuthorCache _cache = new();

    public SkillCommandTests()
    {
        _profile = UserProfile.Create(_userId, "Alice").Value;
        _profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile>>(), Arg.Any<CancellationToken>())
            .Returns(_profile);
    }

    private ProfileCommandContext Context => new(_profiles, _unitOfWork, TestUsers.Authenticated(_userId), _cache);

    [Fact]
    public async Task Skill_without_level_is_added_and_cache_invalidated()
    {
        var result = await new AddSkillCommandHandler(Context)
            .Handle(new AddSkillCommand("C#", null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Level.ShouldBeNull();
        result.Value.Order.ShouldBe(0);
        _profile.Skills.ShouldHaveSingleItem().Name.ShouldBe("C#");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.Invalidated.ShouldBe(["Alice"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Out_of_range_level_is_rejected_by_handler_and_validator(int level)
    {
        var result = await new AddSkillCommandHandler(Context)
            .Handle(new AddSkillCommand("C#", level), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserProfileErrors.SkillLevelOutOfRange);
        _profile.Skills.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.Invalidated.ShouldBeEmpty();

        var validation = new AddSkillCommandValidator().Validate(new AddSkillCommand("C#", level));
        validation.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(AddSkillCommand.Level));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(100)]
    public void Validator_accepts_null_and_boundary_levels(int? level) =>
        new AddSkillCommandValidator().Validate(new AddSkillCommand("C#", level)).IsValid.ShouldBeTrue();

    [Fact]
    public async Task Reorder_and_update_skills()
    {
        var first = _profile.AddSkill("C#", 90).Value;
        var second = _profile.AddSkill("SQL", null).Value;
        var ct = TestContext.Current.CancellationToken;

        (await new ReorderSkillsCommandHandler(Context).Handle(new ReorderSkillsCommand([second.Id, first.Id]), ct))
            .IsSuccess.ShouldBeTrue();
        (second.Order, first.Order).ShouldBe((0, 1));

        (await new UpdateSkillCommandHandler(Context).Handle(new UpdateSkillCommand(first.Id, "C# / .NET", null), ct))
            .IsSuccess.ShouldBeTrue();
        first.Level.ShouldBeNull();

        (await new ReorderSkillsCommandHandler(Context).Handle(new ReorderSkillsCommand([first.Id]), ct))
            .Error.ShouldBe(UserProfileErrors.ReorderMismatch);
    }

    [Fact]
    public async Task Missing_profile_returns_not_found()
    {
        _profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile>>(), Arg.Any<CancellationToken>())
            .Returns((UserProfile?)null);

        var result = await new AddSkillCommandHandler(Context)
            .Handle(new AddSkillCommand("C#", 10), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserProfileErrors.NotFound);
    }

    [Fact]
    public async Task About_me_is_length_checked_before_sanitizing()
    {
        var sanitizer = Substitute.For<IHtmlSanitizer>();
        sanitizer.Sanitize(Arg.Any<string>()).Returns(ci => ci.Arg<string>().Replace("<script>x</script>", ""));
        var handler = new UpdateAboutMeCommandHandler(Context, sanitizer);
        var ct = TestContext.Current.CancellationToken;

        var tooLong = new string('a', UserProfileConstraints.AboutMeMaxLength + 1);
        (await handler.Handle(new UpdateAboutMeCommand(tooLong), ct)).Error.ShouldBe(UserProfileErrors.AboutMeTooLong);
        sanitizer.DidNotReceive().Sanitize(Arg.Any<string>());

        (await handler.Handle(new UpdateAboutMeCommand("<p>Hi</p><script>x</script>"), ct)).IsSuccess.ShouldBeTrue();
        _profile.AboutMe.ShouldBe("<p>Hi</p>");
    }
}
