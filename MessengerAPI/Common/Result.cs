namespace MessengerAPI.Common;

public class Result
{
    public bool IsSuccess { get; protected init; }
    public string? Error { get; protected init; }
    public bool IsFailure => !IsSuccess;

    protected Result() { }

    public static Result Success() => new() { IsSuccess = true };
    public static Result Failure(string error) => new() { IsSuccess = false, Error = error };
}

public sealed class Result<T> : Result
{
    public T? Value { get; private init; }

    private Result() { }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public new static Result<T> Failure(string error) => new() { IsSuccess = false, Error = error };

    /// <summary>
    /// Деконструктор для использования: var (success, data, error) = result;
    /// </summary>
    public void Deconstruct(out bool success, out T? data, out string? error)
    {
        success = IsSuccess;
        data = Value;
        error = Error;
    }

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure) => IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}