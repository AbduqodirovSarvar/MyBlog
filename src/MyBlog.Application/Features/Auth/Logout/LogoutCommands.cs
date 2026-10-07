using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.Logout;

/// <summary>Joriy sessiyani tugatadi. Token noto'g'ri bo'lsa ham muvaffaqiyat (idempotent).</summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand;

/// <summary>Joriy foydalanuvchining barcha qurilmalardagi sessiyalarini tugatadi.</summary>
public sealed record LogoutAllCommand : ICommand;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() =>
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode("Auth.RefreshTokenRequired").WithMessage("Refresh token is required.");
}

internal sealed class LogoutCommandHandler(IRefreshTokenService refreshTokenService) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await refreshTokenService.RevokeAsync(request.RefreshToken, RefreshTokenRevokeReasons.Logout, cancellationToken);
        return Result.Success();
    }
}

internal sealed class LogoutAllCommandHandler(IRefreshTokenService refreshTokenService, ICurrentUser currentUser)
    : ICommandHandler<LogoutAllCommand>
{
    public async Task<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        await refreshTokenService.RevokeAllForUserAsync(currentUser.RequiredId, RefreshTokenRevokeReasons.LogoutAll,
            cancellationToken);
        return Result.Success();
    }
}
