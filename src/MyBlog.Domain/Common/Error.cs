namespace MyBlog.Domain.Common;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    TooManyRequests = 6
}

/// <summary>
/// Xato. <see cref="Code"/> lokalizatsiya kaliti sifatida ishlatiladi,
/// <see cref="Description"/> esa tarjima topilmaganda qaytariladigan default (inglizcha) matn.
/// </summary>
public record Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Lokalizatsiyalangan matnga qo'yiladigan argumentlar ({0}, {1}...).</summary>
    public IReadOnlyList<object> Args { get; init; } = [];

    public static Error Failure(string code, string description) => new(code, description);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public Error WithArgs(params object[] args) => this with { Args = args };
}

public sealed record FieldError(string Field, string Code, string Message);

/// <summary>Bir nechta maydon bo'yicha validatsiya xatolari.</summary>
public sealed record ValidationError(IReadOnlyList<FieldError> Errors)
    : Error("General.Validation", "One or more validation errors occurred.", ErrorType.Validation);
