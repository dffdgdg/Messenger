namespace MessengerShared.Response;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }

    public static ApiResponse<T> Ok(T? data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message,
        Timestamp = DateTime.UtcNow
    };

    public static ApiResponse<T> Fail(string error, string? details = null) => new()
    {
        Success = false,
        Error = error,
        Details = details,
        Timestamp = DateTime.UtcNow
    };
}