namespace MessengerShared.Response;

public static class ApiResponseHelper
{
    public static ApiResponse<T> Success<T>(T data, string? message = null)
        => ApiResponse<T>.Ok(data, message);

    public static ApiResponse<T> Error<T>(string error, string? details = null)
        => ApiResponse<T>.Fail(error, details);

    public static ApiResponse<object> Error(string error, string? details = null)
        => ApiResponse<object>.Fail(error, details);
}