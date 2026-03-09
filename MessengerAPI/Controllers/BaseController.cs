using System.Security.Claims;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<T>(ILogger<T> logger) : ControllerBase where T : class
{
    protected readonly ILogger<T> _logger = logger;

    protected int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Недействительная идентификация пользователя");
        return userId;
    }

    protected bool IsCurrentUser(int userId) => GetCurrentUserId() == userId;

    protected async Task<ActionResult<ApiResponse<TResult>>> ExecuteAsync<TResult>(Func<Task<Result<TResult>>> action, string? successMessage = null)
    {
        var result = await action();

        if (result.IsSuccess)
            return Ok(ApiResponse<TResult>.Ok(result.Value!, successMessage));

        return MapFailure<TResult>(result);
    }

    protected async Task<IActionResult> ExecuteAsync(Func<Task<Result>> action, string? successMessage = null)
    {
        var result = await action();

        if (result.IsSuccess)
            return Ok(ApiResponse<object>.Ok(null, successMessage));

        return MapFailure(result);
    }

    protected ActionResult Forbidden(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(error));

    protected ActionResult<ApiResponse<TData>> Forbidden<TData>(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TData>.Fail(error));

    private ActionResult<ApiResponse<TData>> MapFailure<TData>(Result result)
    {
        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        var response = ApiResponse<TData>.Fail(result.Error!);

        return result.ErrorType switch
        {
            ResultErrorType.Unauthorized => Unauthorized(response),
            ResultErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, response),
            ResultErrorType.NotFound => NotFound(response),
            ResultErrorType.Conflict => Conflict(response),
            ResultErrorType.Internal => StatusCode(StatusCodes.Status500InternalServerError, response),
            _ => BadRequest(response)
        };
    }

    private IActionResult MapFailure(Result result)
    {
        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        var response = ApiResponse<object>.Fail(result.Error!);

        return result.ErrorType switch
        {
            ResultErrorType.Unauthorized => Unauthorized(response),
            ResultErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, response),
            ResultErrorType.NotFound => NotFound(response),
            ResultErrorType.Conflict => Conflict(response),
            ResultErrorType.Internal => StatusCode(StatusCodes.Status500InternalServerError, response),
            _ => BadRequest(response)
        };
    }
}