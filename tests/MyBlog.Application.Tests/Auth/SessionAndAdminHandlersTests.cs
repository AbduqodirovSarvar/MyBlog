using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Features.Auth;
using MyBlog.Application.Features.Auth.Admin;
using MyBlog.Application.Features.Auth.Refresh;
using MyBlog.Domain.Common;
using NSubstitute;

namespace MyBlog.Application.Tests.Auth;

public sealed class RefreshTokenCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();

    private RefreshTokenCommandHandler CreateHandler() => new(_ctx.RefreshTokens, _ctx.Identity, _ctx.SessionIssuer);

    [Fact]
    public async Task Returns_rotated_refresh_token_with_fresh_access_token()
    {
        var user = AuthTestContext.User();
        var rotated = new IssuedRefreshToken("rotated", AuthTestContext.Now.AddDays(14));
        _ctx.RefreshTokens.RotateAsync("old", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RefreshTokenRotation(user.Id, rotated))));
        _ctx.Identity.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("old"), TestContext.Current.CancellationToken);

        result.Value.RefreshToken.ShouldBe("rotated");
        result.Value.AccessToken.ShouldBe("access-token");
        await _ctx.RefreshTokens.DidNotReceiveWithAnyArgs().IssueAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rotation_failure_is_returned()
    {
        _ctx.RefreshTokens.RotateAsync(default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult(Result.Failure<RefreshTokenRotation>(AuthErrors.InvalidRefreshToken)));

        var result = await CreateHandler().Handle(new RefreshTokenCommand("reused"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Blocked_user_loses_all_sessions()
    {
        var user = AuthTestContext.User(isBlocked: true);
        _ctx.RefreshTokens.RotateAsync("old", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RefreshTokenRotation(user.Id, new IssuedRefreshToken("x", AuthTestContext.Now)))));
        _ctx.Identity.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("old"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
        await _ctx.RefreshTokens.Received(1).RevokeAllForUserAsync(user.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

public sealed class AdminUserHandlersTests
{
    private readonly AuthTestContext _ctx = new();
    private readonly Guid _adminId = Guid.CreateVersion7();

    public AdminUserHandlersTests() => _ctx.SignIn(_adminId);

    private Task<Result> BlockAsync(Guid userId) =>
        new BlockUserCommandHandler(_ctx.CurrentUser, _ctx.Identity, _ctx.RefreshTokens)
            .Handle(new BlockUserCommand(userId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Cannot_block_self()
    {
        (await BlockAsync(_adminId)).Error.ShouldBe(AuthErrors.CannotBlockSelf);
    }

    [Fact]
    public async Task Cannot_block_super_admin()
    {
        var target = AuthTestContext.User(roles: Roles.SuperAdmin);
        _ctx.Identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        (await BlockAsync(target.Id)).Error.ShouldBe(AuthErrors.CannotBlockSuperAdmin);
        await _ctx.Identity.DidNotReceiveWithAnyArgs().SetBlockedAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Only_super_admin_can_block_admin()
    {
        var target = AuthTestContext.User(roles: Roles.Admin);
        _ctx.Identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        (await BlockAsync(target.Id)).Error.ShouldBe(AuthErrors.CannotBlockAdmin);
    }

    [Fact]
    public async Task Blocking_revokes_refresh_tokens()
    {
        var target = AuthTestContext.User();
        _ctx.Identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _ctx.Identity.SetBlockedAsync(target.Id, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));

        (await BlockAsync(target.Id)).IsSuccess.ShouldBeTrue();
        await _ctx.RefreshTokens.Received(1).RevokeAllForUserAsync(target.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cannot_remove_role_from_last_super_admin()
    {
        var target = AuthTestContext.User(roles: Roles.SuperAdmin);
        _ctx.Identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _ctx.Identity.CountUsersInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns(1);

        var result = await new RemoveRoleCommandHandler(_ctx.Identity)
            .Handle(new RemoveRoleCommand(target.Id, "superadmin"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.CannotRemoveLastSuperAdmin);
        await _ctx.Identity.DidNotReceiveWithAnyArgs().RemoveFromRoleAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Role_validator_rejects_unknown_roles()
    {
        var validator = new AssignRoleCommandValidator();

        validator.Validate(new AssignRoleCommand(Guid.NewGuid(), "admin")).IsValid.ShouldBeTrue();
        validator.Validate(new AssignRoleCommand(Guid.NewGuid(), "Owner")).Errors
            .ShouldContain(e => e.ErrorCode == "Auth.RoleNotFound");
    }
}
