using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MyBlog.Api.Common;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Api.Infrastructure;

/// <summary>Kutilmagan exception'larni lokalizatsiyalangan ProblemDetails'ga aylantiradi.</summary>
internal sealed class GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
            return false;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Mijoz so'rovni bekor qildi — log va body shart emas.
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (error, status) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning("Request {Method} {Path} failed with {ExceptionType}: {Message}",
                httpContext.Request.Method, httpContext.Request.Path, exception.GetType().Name, exception.Message);

        var detail = environment.IsDevelopment() ? exception.ToString() : null;
        await ErrorProblemDetails.WriteAsync(httpContext, error, status, detail, cancellationToken);
        return true;
    }

    private static (Error Error, int Status) Map(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => (GeneralErrors.ConcurrencyConflict, StatusCodes.Status409Conflict),
        UnauthorizedAccessException => (GeneralErrors.Unauthorized, StatusCodes.Status401Unauthorized),
        ForbiddenOwnershipException => (GeneralErrors.Forbidden, StatusCodes.Status403Forbidden),
        BadHttpRequestException bad => (Error.Validation("General.Validation", "The request is invalid."), bad.StatusCode),
        _ => (GeneralErrors.ServerError, StatusCodes.Status500InternalServerError)
    };
}
