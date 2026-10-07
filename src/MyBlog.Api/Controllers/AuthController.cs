using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Api.Common;
using MyBlog.Api.RateLimiting;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Auth.ChangePassword;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Application.Features.Auth.ConfirmEmail;
using MyBlog.Application.Features.Auth.ForgotPassword;
using MyBlog.Application.Features.Auth.GetCurrentUser;
using MyBlog.Application.Features.Auth.Login;
using MyBlog.Application.Features.Auth.Logout;
using MyBlog.Application.Features.Auth.Refresh;
using MyBlog.Application.Features.Auth.Register;
using MyBlog.Application.Features.Auth.ResendConfirmation;
using MyBlog.Application.Features.Auth.ResetPassword;

namespace MyBlog.Api.Controllers;

/// <summary>Ro'yxatdan o'tish, kirish, token yangilash va parol bilan bog'liq amallar.</summary>
[Route("api/auth")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ErrorProblemDetails.ContentType)]
public sealed class AuthController(ISender sender) : ApiController(sender)
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Register(RegisterCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    /// <summary>Har doim bir xil javob (email mavjudligi oshkor qilinmaydi).</summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    /// <summary>Refresh token rotatsiyasi: eski token bekor qilinadi, yangi juftlik qaytadi.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Refresh(RefreshTokenCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Logout(LogoutCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken) =>
        (await Sender.Send(new LogoutAllCommand(), cancellationToken)).ToActionResult();

    /// <summary>Har doim bir xil javob (email mavjudligi oshkor qilinmaydi).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    /// <summary>Barcha sessiyalar bekor qilinadi; joriy qurilma uchun yangi token juftligi qaytadi.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> ChangePassword(ChangePasswordCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToActionResult();

    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        (await Sender.Send(new GetCurrentUserQuery(), cancellationToken)).ToActionResult();
}
