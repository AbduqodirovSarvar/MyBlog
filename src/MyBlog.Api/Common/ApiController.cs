using Microsoft.AspNetCore.Mvc;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Domain.Common;

namespace MyBlog.Api.Common;

/// <summary>
/// Barcha controller'lar uchun asos. Controller'lar yupqa: so'rovni <see cref="Sender"/> orqali
/// Application'ga yuboradi va Result'ni <see cref="ResultExtensions"/> bilan HTTP javobga aylantiradi.
/// </summary>
[ApiController]
public abstract class ApiController(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    /// <summary>Error → lokalizatsiyalangan ProblemDetails.</summary>
    protected IActionResult Problem(Error error) => new ErrorActionResult(error);
}

/// <summary>Xatoni bajarilish paytida (HttpContext mavjud bo'lganda) ProblemDetails'ga aylantiradi.</summary>
public sealed class ErrorActionResult(Error error) : IActionResult
{
    public Error Error { get; } = error;

    public Task ExecuteResultAsync(ActionContext context)
    {
        var problem = ErrorProblemDetails.Create(context.HttpContext, Error);
        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add(ErrorProblemDetails.ContentType);
        return result.ExecuteResultAsync(context);
    }
}

public static class ResultExtensions
{
    /// <summary>Success → 204 No Content.</summary>
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : new ErrorActionResult(result.Error);

    /// <summary>Success → 200 OK (qiymat bilan).</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : new ErrorActionResult(result.Error);

    /// <summary>Success → 201 Created, Location — berilgan URL.</summary>
    public static IActionResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? new CreatedResult(location(result.Value), result.Value) : new ErrorActionResult(result.Error);

    /// <summary>Success → 201 Created, Location — action va route qiymatlaridan.</summary>
    public static IActionResult ToCreatedResult<T>(this Result<T> result, string actionName, Func<T, object?> routeValues,
        string? controllerName = null) =>
        result.IsSuccess
            ? new CreatedAtActionResult(actionName, controllerName, routeValues(result.Value), result.Value)
            : new ErrorActionResult(result.Error);
}
