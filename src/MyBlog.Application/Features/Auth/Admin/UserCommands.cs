using FluentValidation;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.Admin;

public sealed record BlockUserCommand(Guid UserId) : ICommand;

public sealed record UnblockUserCommand(Guid UserId) : ICommand;

public sealed record AssignRoleCommand(Guid UserId, string Role) : ICommand;

public sealed record RemoveRoleCommand(Guid UserId, string Role) : ICommand;

internal sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator() =>
        RuleFor(x => x.Role).Must(r => RoleNames.Normalize(r) is not null)
            .WithErrorCode("Auth.RoleNotFound").WithMessage("The role does not exist.");
}

internal sealed class RemoveRoleCommandValidator : AbstractValidator<RemoveRoleCommand>
{
    public RemoveRoleCommandValidator() =>
        RuleFor(x => x.Role).Must(r => RoleNames.Normalize(r) is not null)
            .WithErrorCode("Auth.RoleNotFound").WithMessage("The role does not exist.");
}

/// <summary>
/// O'zini va SuperAdmin'ni bloklab bo'lmaydi; administratorni faqat SuperAdmin bloklaydi.
/// Bloklanganda barcha refresh token'lar bekor qilinadi; security stamp yangilangani uchun access token'lar ham
/// keyingi so'rovdayoq rad etiladi.
/// </summary>
internal sealed class BlockUserCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    IRefreshTokenService refreshTokenService) : ICommandHandler<BlockUserCommand>
{
    public async Task<Result> Handle(BlockUserCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == currentUser.RequiredId)
            return AuthErrors.CannotBlockSelf;

        var user = await identityService.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        if (user.Roles.Contains(Roles.SuperAdmin))
            return AuthErrors.CannotBlockSuperAdmin;

        if (user.Roles.Contains(Roles.Admin) && !currentUser.IsInRole(Roles.SuperAdmin))
            return AuthErrors.CannotBlockAdmin;

        if (user.IsBlocked)
            return Result.Success();

        var blocked = await identityService.SetBlockedAsync(user.Id, blocked: true, cancellationToken);
        if (blocked.IsFailure)
            return blocked;

        await refreshTokenService.RevokeAllForUserAsync(user.Id, RefreshTokenRevokeReasons.Blocked, cancellationToken);
        return Result.Success();
    }
}

internal sealed class UnblockUserCommandHandler(IIdentityService identityService) : ICommandHandler<UnblockUserCommand>
{
    public async Task<Result> Handle(UnblockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        return user.IsBlocked
            ? await identityService.SetBlockedAsync(user.Id, blocked: false, cancellationToken)
            : Result.Success();
    }
}

/// <summary>
/// Rol o'zgarishi security stamp'ni yangilaydi: eski access token 401 oladi, mijoz refresh qilib yangi rollar bilan
/// token oladi.
/// </summary>
internal sealed class AssignRoleCommandHandler(IIdentityService identityService) : ICommandHandler<AssignRoleCommand>
{
    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var role = RoleNames.Normalize(request.Role)!;
        var user = await identityService.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        return user.Roles.Contains(role)
            ? Result.Success()
            : await identityService.AddToRoleAsync(user.Id, role, cancellationToken);
    }
}

internal sealed class RemoveRoleCommandHandler(IIdentityService identityService) : ICommandHandler<RemoveRoleCommand>
{
    public async Task<Result> Handle(RemoveRoleCommand request, CancellationToken cancellationToken)
    {
        var role = RoleNames.Normalize(request.Role)!;
        var user = await identityService.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        if (!user.Roles.Contains(role))
            return Result.Success();

        if (role == Roles.SuperAdmin && await identityService.CountUsersInRoleAsync(Roles.SuperAdmin, cancellationToken) <= 1)
            return AuthErrors.CannotRemoveLastSuperAdmin;

        return await identityService.RemoveFromRoleAsync(user.Id, role, cancellationToken);
    }
}
