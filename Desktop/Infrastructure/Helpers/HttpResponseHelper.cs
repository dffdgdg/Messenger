using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Desktop.Infrastructure.Helpers;

public static class HttpResponseHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string TryExtractErrorMessage(string json, HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<ApiResponse<object>>(json, JsonOptions);
                if (!string.IsNullOrWhiteSpace(errorResponse?.Error))
                    return errorResponse.Error;
            }
            catch (JsonException) { }
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "Неверный логин или пароль",
            HttpStatusCode.Forbidden => "Доступ запрещён. Обратитесь к администратору",
            HttpStatusCode.TooManyRequests => "Слишком много попыток. Подождите минуту",
            HttpStatusCode.BadRequest => "Некорректный запрос",
            HttpStatusCode.InternalServerError => "Внутренняя ошибка сервера",
            HttpStatusCode.ServiceUnavailable => "Сервер недоступен",
            _ => $"Ошибка сервера ({(int)statusCode})"
        };
    }
}