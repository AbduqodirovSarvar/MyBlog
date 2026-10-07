using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Net.Http.Headers;
using MyBlog.Api.Common;

namespace MyBlog.Api.Authorization;

/// <summary>
/// 401/403 javoblari ham boshqa xatolar kabi lokalizatsiyalangan ProblemDetails ko'rinishida qaytadi
/// (default holatda body bo'sh bo'ladi). Sxemaga bog'liq emas.
/// </summary>
internal sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
            await ErrorProblemDetails.WriteAsync(context, GeneralErrors.Unauthorized, cancellationToken: context.RequestAborted);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            // Token bor, lekin yaroqsiz/muddati o'tgan bo'lsa ham autentifikatsiya qilinmagan hisoblanadi.
            var error = context.User.Identity?.IsAuthenticated == true ? GeneralErrors.Forbidden : GeneralErrors.Unauthorized;
            await ErrorProblemDetails.WriteAsync(context, error, cancellationToken: context.RequestAborted);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
