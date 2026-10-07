using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;

namespace MyBlog.Api.Common;

/// <summary>
/// Error → ProblemDetails (RFC 9457). Barcha javoblar (controller, exception handler, rate limiter) bir xil ko'rinishda:
/// title — lokalizatsiyalangan matn, extensions: code, traceId; validatsiyada errors (maydon → xabarlar) va codes.
/// </summary>
internal static class ErrorProblemDetails
{
    public const string ContentType = "application/problem+json";

    public static int StatusCodeOf(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemDetails Create(HttpContext httpContext, Error error, int? statusCode = null, string? detail = null)
    {
        var localizer = httpContext.RequestServices.GetRequiredService<ILocalizer>();
        var status = statusCode ?? StatusCodeOf(error.Type);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = localizer.Get(error.Code, error.Description, [.. error.Args]),
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path
        };

        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (error is ValidationError validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
            problem.Extensions["codes"] = validation.Errors
                .GroupBy(e => e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray());
        }

        return problem;
    }

    public static Task WriteAsync(HttpContext httpContext, Error error, int? statusCode = null, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var problem = Create(httpContext, error, statusCode, detail);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, ContentType, cancellationToken);
    }
}

/// <summary>Umumiy xatolar (kalitlari common.*.json'da).</summary>
internal static class GeneralErrors
{
    public static readonly Error NotFound = Error.NotFound("General.NotFound", "The requested resource was not found.");
    public static readonly Error Unauthorized = Error.Unauthorized("General.Unauthorized", "Please sign in first.");
    public static readonly Error Forbidden = Error.Forbidden("General.Forbidden", "You do not have permission to perform this action.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("General.ConcurrencyConflict", "The data was changed by another user.");
    public static readonly Error ServerError = Error.Failure("General.ServerError", "An unexpected server error occurred.");
    public static readonly Error TooManyRequests = new("General.TooManyRequests", "Too many requests.", ErrorType.TooManyRequests);
}
