using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MessengerAPI.Common;

public enum ResultErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Internal
}

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

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };

    public new static Result<T> Failure(string error, ResultErrorType type = ResultErrorType.Validation)
        => new() { IsSuccess = false, Error = error, ErrorType = type };

    public new static Result<T> NotFound(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.NotFound };

    public new static Result<T> Forbidden(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Forbidden };

    public new static Result<T> Unauthorized(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Unauthorized };

    public new static Result<T> Conflict(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Conflict };

    public new static Result<T> Internal(string error)
        => new() { IsSuccess = false, Error = error, ErrorType = ResultErrorType.Internal };

    public new static Result<T> FromFailure(Result result)
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

public static class ResultExtensions
{
    /// <summary>
    /// Извлекает значение или возвращает null с логированием ошибки.
    /// Для reference types в Hub/fire-and-forget контекстах.
    /// </summary>
    public static T? UnwrapOrDefault<T>(
        this Result<T> result,
        ILogger logger,
        [CallerMemberName] string caller = "") where T : class
    {
        if (result.IsSuccess)
            return result.Value;

        logger.LogWarning("{Method} failed: {Error}", caller, result.Error);
        return default;
    }

    /// <summary>
    /// Извлекает значение или возвращает fallback с логированием ошибки.
    /// Для value types и случаев с осмысленным default.
    /// </summary>
    public static T UnwrapOrFallback<T>(
        this Result<T> result,
        T fallback,
        ILogger logger,
        [CallerMemberName] string caller = "")
    {
        if (result.IsSuccess)
            return result.Value!;

        logger.LogWarning("{Method} failed: {Error}", caller, result.Error);
        return fallback;
    }

    /// <summary>
    /// Try-паттерн для Result. Возвращает true при успехе и out-значение.
    /// </summary>
    public static bool TryUnwrap<T>(
        this Result<T> result,
        [NotNullWhen(true)] out T? value,
        ILogger? logger = null,
        [CallerMemberName] string caller = "")
    {
        if (result.IsSuccess && result.Value is not null)
        {
            value = result.Value;
            return true;
        }

        logger?.LogWarning("{Method} failed: {Error}", caller, result.Error);
        value = default;
        return false;
    }
}