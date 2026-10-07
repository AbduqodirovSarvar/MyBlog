using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Common.Behaviors;

/// <summary>So'rov nomi, davomiyligi va xato natijalarni loglaydi; sekin so'rovlarni alohida belgilaydi.</summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    private const int SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        var response = await next();

        stopwatch.Stop();

        if (response is Result { IsFailure: true } failed)
            logger.LogWarning("Request {RequestName} failed with {ErrorCode} in {ElapsedMs} ms",
                requestName, failed.Error.Code, stopwatch.ElapsedMilliseconds);
        else if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);
        else
            logger.LogDebug("Request {RequestName} handled in {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
