using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Auth;
using MyBlog.Application.Features.Auth.ChangePassword;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Application.Features.Auth.ForgotPassword;
using MyBlog.Application.Features.Auth.ResendConfirmation;
using MyBlog.Application.Features.Auth.ResetPassword;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Auth;

public sealed class ForgotPasswordCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();

    private Task<Result<MessageResponse>> SendAsync(string email) =>
        new ForgotPasswordCommandHandler(_ctx.Identity, _ctx.EmailService, _ctx.Localizer)
            .Handle(new ForgotPasswordCommand(email), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Response_is_identical_for_existing_and_unknown_email()
    {
        var user = AuthTestContext.User();
        _ctx.Identity.FindByEmailAsync("ali@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _ctx.Identity.GeneratePasswordResetTokenAsync(user.Id, Arg.Any<CancellationToken>()).Returns("reset-token");

        var existing = await SendAsync("ali@example.com");
        var unknown = await SendAsync("nobody@example.com");

        existing.IsSuccess.ShouldBeTrue();
        unknown.IsSuccess.ShouldBeTrue();
        unknown.Value.ShouldBe(existing.Value);
        _ctx.QueuedEmails().ShouldHaveSingleItem().To.ShouldBe("ali@example.com");
    }

    [Fact]
    public async Task Reset_link_uses_user_preferred_culture_and_carries_email_and_token()
    {
        var user = AuthTestContext.User();
        _ctx.Identity.FindByEmailAsync("ali@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _ctx.Identity.GeneratePasswordResetTokenAsync(user.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        _ctx.Profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile, ProfileSummary>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProfileSummary?>(new ProfileSummary("Ali", null, "en")));

        await SendAsync("ali@example.com");

        await _ctx.Renderer.Received(1).RenderAsync("reset-password", "en",
            Arg.Is<IReadOnlyDictionary<string, string>>(v =>
                v["link"] == "https://myblog.uz/en/auth/reset-password?email=ali%40example.com&token=reset-token"
                && v["minutes"] == "60"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unconfirmed_or_blocked_users_get_no_email()
    {
        _ctx.Identity.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>())
            .Returns(AuthTestContext.User(emailConfirmed: false));
        _ctx.Identity.FindByEmailAsync("b@example.com", Arg.Any<CancellationToken>())
            .Returns(AuthTestContext.User(isBlocked: true));

        (await SendAsync("a@example.com")).IsSuccess.ShouldBeTrue();
        (await SendAsync("b@example.com")).IsSuccess.ShouldBeTrue();

        _ctx.QueuedEmails().ShouldBeEmpty();
    }
}

public sealed class ResendConfirmationCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();

    [Fact]
    public async Task Sends_only_to_unconfirmed_users_with_same_response()
    {
        var unconfirmed = AuthTestContext.User(emailConfirmed: false);
        _ctx.Identity.FindByEmailAsync("new@example.com", Arg.Any<CancellationToken>()).Returns(unconfirmed);
        _ctx.Identity.FindByEmailAsync("old@example.com", Arg.Any<CancellationToken>()).Returns(AuthTestContext.User());
        _ctx.Identity.GenerateEmailConfirmationTokenAsync(unconfirmed.Id, Arg.Any<CancellationToken>()).Returns("t");
        var handler = new ResendConfirmationCommandHandler(_ctx.Identity, _ctx.EmailService, _ctx.Localizer);
        var ct = TestContext.Current.CancellationToken;

        var first = await handler.Handle(new ResendConfirmationCommand("new@example.com"), ct);
        var second = await handler.Handle(new ResendConfirmationCommand("old@example.com"), ct);
        var third = await handler.Handle(new ResendConfirmationCommand("none@example.com"), ct);

        second.Value.ShouldBe(first.Value);
        third.Value.ShouldBe(first.Value);
        _ctx.QueuedEmails().ShouldHaveSingleItem().Subject.ShouldBe("confirm-email");
    }
}

public sealed class ResetPasswordCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();
    private readonly AuthUser _user = AuthTestContext.User();

    private Task<Result> ResetAsync() =>
        new ResetPasswordCommandHandler(_ctx.Identity, _ctx.RefreshTokens, _ctx.EmailService)
            .Handle(new ResetPasswordCommand("ali@example.com", "token", "NewSecret1", "NewSecret1"),
                TestContext.Current.CancellationToken);

    [Fact]
    public async Task Success_revokes_all_refresh_tokens_and_sends_notification()
    {
        _ctx.Identity.FindByEmailAsync("ali@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _ctx.Identity.ResetPasswordAsync(_user.Id, "token", "NewSecret1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var result = await ResetAsync();

        result.IsSuccess.ShouldBeTrue();
        await _ctx.RefreshTokens.Received(1).RevokeAllForUserAsync(_user.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _ctx.QueuedEmails().ShouldHaveSingleItem().Subject.ShouldBe("password-changed");
    }

    [Fact]
    public async Task Invalid_token_does_not_revoke_sessions()
    {
        _ctx.Identity.FindByEmailAsync("ali@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _ctx.Identity.ResetPasswordAsync(default, default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult(Result.Failure(AuthErrors.InvalidToken)));

        (await ResetAsync()).Error.ShouldBe(AuthErrors.InvalidToken);
        await _ctx.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, default!, TestContext.Current.CancellationToken);
        _ctx.QueuedEmails().ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_email_returns_invalid_token_error()
    {
        (await ResetAsync()).Error.ShouldBe(AuthErrors.InvalidToken);
    }
}

public sealed class ChangePasswordCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();

    [Fact]
    public async Task Success_revokes_all_sessions_and_issues_new_pair()
    {
        var user = AuthTestContext.User();
        _ctx.SignIn(user.Id);
        _ctx.Identity.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _ctx.Identity.ChangePasswordAsync(user.Id, "OldSecret1", "NewSecret1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var handler = new ChangePasswordCommandHandler(_ctx.CurrentUser, _ctx.Identity, _ctx.RefreshTokens,
            _ctx.SessionIssuer, _ctx.EmailService);

        var result = await handler.Handle(new ChangePasswordCommand("OldSecret1", "NewSecret1", "NewSecret1"),
            TestContext.Current.CancellationToken);

        result.Value.RefreshToken.ShouldBe("refresh-token");
        Received.InOrder(() =>
        {
            _ctx.RefreshTokens.RevokeAllForUserAsync(user.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
            _ctx.RefreshTokens.IssueAsync(user.Id, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task New_access_token_carries_the_updated_session_version()
    {
        var user = AuthTestContext.User();
        var refreshed = user with { SessionVersion = "session-v2" };
        _ctx.SignIn(user.Id);
        _ctx.Identity.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user, refreshed);
        _ctx.Identity.ChangePasswordAsync(user.Id, "OldSecret1", "NewSecret1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var handler = new ChangePasswordCommandHandler(_ctx.CurrentUser, _ctx.Identity, _ctx.RefreshTokens,
            _ctx.SessionIssuer, _ctx.EmailService);

        (await handler.Handle(new ChangePasswordCommand("OldSecret1", "NewSecret1", "NewSecret1"),
            TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        _ctx.Tokens.Received(1).CreateAccessToken(Arg.Is<AuthUser>(u => u.SessionVersion == "session-v2"));
    }
}
