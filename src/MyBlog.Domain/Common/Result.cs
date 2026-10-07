namespace MyBlog.Domain.Common;

/// <summary>
/// Generic pipeline behavior'lar xato natijani tiplarga bog'lanmasdan yarata olishi uchun.
/// </summary>
public interface IResultFactory<TSelf> where TSelf : IResultFactory<TSelf>
{
    static abstract TSelf Failure(Error error);
}

public class Result : IResultFactory<Result>
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error.Code.Length != 0)
            throw new InvalidOperationException("Successful result cannot contain an error.");
        if (!isSuccess && error.Code.Length == 0)
            throw new InvalidOperationException("Failed result must contain an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result, IResultFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) { }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    static Result<T> IResultFactory<Result<T>>.Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}
