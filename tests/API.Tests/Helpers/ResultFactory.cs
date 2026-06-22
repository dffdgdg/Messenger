using API.Domain.Common;

namespace API.Tests.Helpers;

public static class ResultFactory
{
    public static Result<T> CreateByStatusCode<T>(int statusCode, string errorMessage)
    {
        return statusCode switch
        {
            401 => Result<T>.Unauthorized(errorMessage),
            403 => Result<T>.Forbidden(errorMessage),
            404 => Result<T>.NotFound(errorMessage),
            409 => Result<T>.Conflict(errorMessage),
            500 => Result<T>.Internal(errorMessage),
            _ => throw new ArgumentException($"Unsupported status code: {statusCode}", nameof(statusCode))
        };
    }

    public static Result CreateByStatusCode(int statusCode, string errorMessage)
    {
        return statusCode switch
        {
            401 => Result.Unauthorized(errorMessage),
            403 => Result.Forbidden(errorMessage),
            404 => Result.NotFound(errorMessage),
            409 => Result.Conflict(errorMessage),
            500 => Result.Internal(errorMessage),
            _ => throw new ArgumentException($"Unsupported status code: {statusCode}", nameof(statusCode))
        };
    }
}