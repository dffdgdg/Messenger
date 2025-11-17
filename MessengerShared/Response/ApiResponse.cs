namespace MessengerShared.Response
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
