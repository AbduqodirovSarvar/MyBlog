using FluentValidation;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() =>
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode("Auth.RefreshTokenRequired").WithMessage("Refresh token is required.")
            .MaximumLength(512).WithErrorCode("Auth.InvalidRefreshToken").WithMessage("The session has expired. Please sign in again.");
}

/// <summary>Rotatsiya: eski token bekor qilinadi, yangi juftlik qaytadi. Rollar/ruxsatlar bazadan qayta o'qiladi.</summary>
internal sealed class RefreshTokenCommandHandler(
    IRefreshTokenService refreshTokenService,
    IIdentityService identityService,
    AuthSessionIssuer sessionIssuer) : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var rotation = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken);
        if (rotation.IsFailure)
            return rotation.Error;

        var user = await identityService.FindByIdAsync(rotation.Value.UserId, cancellationToken);
        if (user is null || user.IsBlocked)
        {
            await refreshTokenService.RevokeAllForUserAsync(rotation.Value.UserId, RefreshTokenRevokeReasons.UserUnavailable,
                cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        return await sessionIssuer.CreateResponseAsync(user, rotation.Value.Token, cancellationToken);
    }
}
