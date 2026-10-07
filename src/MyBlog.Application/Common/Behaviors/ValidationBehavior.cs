using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Common.Behaviors;

/// <summary>
/// FluentValidation validator'larini ishga tushiradi. Xato bo'lsa handler chaqirilmaydi va
/// <see cref="ValidationError"/> qaytariladi. Validator'da WithErrorCode bilan berilgan kod
/// lokalizatsiya faylida bo'lsa, xabar shu yerda tarjima qilinadi.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILocalizer localizer)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorList = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (validatorList.Length == 0)
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => new FieldError(
                ToCamelCase(f.PropertyName),
                f.ErrorCode,
                localizer.Find(f.ErrorCode) ?? f.ErrorMessage))
            .DistinctBy(e => (e.Field, e.Code))
            .ToList();

        return errors.Count == 0
            ? await next()
            : TResponse.Failure(new ValidationError(errors));
    }

    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
