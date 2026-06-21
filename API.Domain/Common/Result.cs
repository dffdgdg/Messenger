namespace API.Domain.Common;

public enum ResultErrorType { Validation, Unauthorized, Forbidden, NotFound, Conflict, Internal }

public class Result
{
    public bool IsSuccess { get; protected init; }
    public string? Error { get; protected init; }
    public ResultErrorType? ErrorType { get; protected init; }
    public bool IsFailure => !IsSuccess;

    protected Result() { }

    public static Result Success() => new() { IsSuccess = true };

    public static Result Failure(string error, ResultErrorType type = ResultErrorType.Validation)
        => new() { IsSuccess = false, Error = error, ErrorType = type };

    public static Result NotFound(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.NotFound };

    public static Result Forbidden(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Forbidden };

    public static Result Unauthorized(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Unauthorized };

    public static Result Conflict(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Conflict };

    public static Result Internal(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Internal };

    public static Result FromFailure(Result result)
        => new() { IsSuccess = false, Error = result.Error, ErrorType = result.ErrorType };
}

public sealed class Result<T> : Result
{
    public T? Value { get; private init; }

    private Result() { }

    public static new Result<T> Success(T value) => new() { IsSuccess = true, Value = value };

    public static new Result<T> Failure(string error, ResultErrorType type = ResultErrorType.Validation)
        => new() { IsSuccess = false, Error = error, ErrorType = type };

    public static new Result<T> NotFound(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.NotFound };

    public static new Result<T> Forbidden(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Forbidden };

    public static new Result<T> Unauthorized(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Unauthorized };

    public static new Result<T> Conflict(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Conflict };

    public static new Result<T> Internal(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Internal };

    public static new Result<T> FromFailure(Result result)
        => new() { IsSuccess = false, Error = result.Error, ErrorType = result.ErrorType };

    public void Deconstruct(out bool success, out T? data, out string? error)
    {
        success = IsSuccess;
        data = Value;
        error = Error;
    }

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}