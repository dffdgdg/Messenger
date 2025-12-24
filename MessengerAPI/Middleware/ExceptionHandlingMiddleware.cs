using MessengerShared.Response;
using System.Text.Json;

namespace MessengerAPI.Middleware
{
    public class ExceptionHandlingMiddleware(RequestDelegate next,ILogger<ExceptionHandlingMiddleware> logger,IWebHostEnvironment env)
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var (statusCode, message) = exception switch
            {
                ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
                UnauthorizedAccessException ex => (StatusCodes.Status401Unauthorized, ex.Message),
                KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
                InvalidOperationException ex => (StatusCodes.Status400BadRequest, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Произошла внутренняя ошибка")
            };

            if (statusCode >= 500)
            {
                logger.LogError(exception, "Необработанное исключение: {Message}", exception.Message);
            }
            else
            {
                logger.LogWarning(exception, "Ошибка запроса: {Message}", exception.Message);
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<object>
            {
                Success = false,
                Error = message,
                Timestamp = DateTime.Now
            };

            if (env.IsDevelopment() && statusCode >= 500)
            {
                response.Details = exception.ToString();
            }

            var json = JsonSerializer.Serialize(response, JsonOptions);
            await context.Response.WriteAsync(json);
        }
    }

    public static class ExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) => app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}