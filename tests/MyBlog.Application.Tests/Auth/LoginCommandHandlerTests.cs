using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Auth;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Application.Features.Auth.Login;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Auth;

public sealed class LoginCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();
    private readonly AuthUser _user = AuthTestContext.User();

    private LoginCommandHandler CreateHandler() => new(_ctx.Identity, _ctx.SessionIssuer);

    private void ArrangeCheck(PasswordCheckResult check)
    {
        _ctx.Identity.CheckCredentialsAsync("ali", "Secret123", Arg.Any<CancellationToken>()).Returns(check);
        _ctx.Identity.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    private Task<MyBlog.Domain.Common.Result<AuthResponse>> LoginAsync() =>
        CreateHandler().Handle(new LoginCommand(" ali ", "Secret123"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Unknown_user_gets_same_generic_error_as_wrong_password()
    {
        // Infrastructure topilmagan foydalanuvchi uchun ham InvalidPassword qaytaradi (soxta xesh tekshiruvidan keyin).
        ArrangeCheck(new PasswordCheckResult(PasswordCheckStatus.InvalidPassword));

        (await LoginAsync()).Error.ShouldBe(AuthErrors.InvalidCredentials);
        await _ctx.Identity.DidNotReceiveWithAnyArgs().FindByIdAsync(default, TestContext.Current.CancellationToken);
        await _ctx.RefreshTokens.DidNotReceiveWithAnyArgs().IssueAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Locked_out_user_gets_generic_error_without_revealing_password_validity()
    {
        ArrangeCheck(new PasswordCheckResult(PasswordCheckStatus.LockedOut));

        (await LoginAsync()).Error.ShouldBe(AuthErrors.InvalidCredentials);
        await _ctx.RefreshTokens.DidNotReceiveWithAnyArgs().IssueAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Blocked_user_is_rejected()
    {
        ArrangeCheck(new PasswordCheckResult(PasswordCheckStatus.Blocked));

        (await LoginAsync()).Error.ShouldBe(AuthErrors.UserBlocked);
    }

    [Fact]
    public async Task Unconfirmed_email_is_rejected()
    {
        ArrangeCheck(new PasswordCheckResult(PasswordCheckStatus.EmailNotConfirmed));

        (await LoginAsync()).Error.ShouldBe(AuthErrors.EmailNotConfirmed);
    }

    [Fact]
    public async Task User_deleted_between_check_and_load_gets_generic_error()
    {
        _ctx.Identity.CheckCredentialsAsync("ali", "Secret123", Arg.Any<CancellationToken>())
            .Returns(PasswordCheckResult.Succeeded(_user.Id));

        (await LoginAsync()).Error.ShouldBe(AuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Success_issues_token_pair_and_updates_last_login()
    {
        ArrangeCheck(PasswordCheckResult.Succeeded(_user.Id));
        var avatarId = Guid.CreateVersion7();
        _ctx.Profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile, ProfileSummary>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProfileSummary?>(new ProfileSummary("Ali Valiyev", avatarId, "uz")));
        _ctx.Media.FirstOrDefaultAsync(Arg.Any<ISpecification<MediaFile, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("2026/10/avatar.webp"));
        _ctx.FileStorage.GetPublicUrl("2026/10/avatar.webp").Returns("/media/2026/10/avatar.webp");

        var result = await LoginAsync();

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;
        response.AccessToken.ShouldBe("access-token");
        response.RefreshToken.ShouldBe("refresh-token");
        response.RefreshTokenExpiresAt.ShouldBe(AuthTestContext.Now.AddDays(14));
        response.User.Id.ShouldBe(_user.Id);
        response.User.UserName.ShouldBe("ali");
        response.User.DisplayName.ShouldBe("Ali Valiyev");
        response.User.AvatarUrl.ShouldBe("/media/2026/10/avatar.webp");
        response.User.Roles.ShouldBe(_user.Roles);
        response.User.Permissions.ShouldBe(_user.Permissions);

        await _ctx.Identity.Received(1).UpdateLastLoginAsync(_user.Id, Arg.Any<CancellationToken>());
        await _ctx.RefreshTokens.Received(1).IssueAsync(_user.Id, Arg.Any<CancellationToken>());
    }
}
