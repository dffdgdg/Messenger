namespace MessengerShared.Response
{
    public static class ApiResponseHelper
    {
        public static ApiResponse<T> Success<T>(T data, string? message = null)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.Now
            };
        }

        public static ApiResponse<T> Error<T>(string error, string? details = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = error,
                Details = details,
                Timestamp = DateTime.Now
            };
        }

        public static ApiResponse Success(string? message = null)
        {
            return new ApiResponse
            {
                Success = true,
                Message = message,
                Timestamp = DateTime.Now
            };
        }

        public static ApiResponse Error(string error, string? details = null)
        {
            return new ApiResponse
            {
                Success = false,
                Error = error,
                Details = details,
                Timestamp = DateTime.Now
            };
        }
    }
}