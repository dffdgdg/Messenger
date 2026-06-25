using API.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Shared.Infrastructure;
using System.Security.Claims;

namespace API.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<T>(ILogger<T> logger) : ControllerBase
    where T : class
{
    protected readonly ILogger<T> _logger = logger;

    protected int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Недействительная идентификация пользователя");

        return userId;
    }

    protected bool IsCurrentUser(int userId)
        => GetCurrentUserId() == userId;

    protected IActionResult Map<TResult>(Result<TResult> result)
    {
        if (result.IsSuccess)
            return Ok(ApiResponse<TResult>.Ok(result.Value));

        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        return MapFailure<TResult>(result);
    }

    protected IActionResult Map(Result result)
    {
        if (result.IsSuccess)
            return Ok(ApiResponse<object>.Ok(null));

        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        return MapFailure<object>(result);
    }

    protected IActionResult Forbidden(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(error));

    protected IActionResult Forbidden<TData>(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TData>.Fail(error));

    protected async Task<IActionResult> ExecuteAsync<TResult>(Func<Task<Result<TResult>>> action, string? successMessage = null)
    {
        var result = await action();

        if (result.IsSuccess)
            return Ok(ApiResponse<TResult>.Ok(result.Value!, successMessage));

        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        return MapFailure<TResult>(result);
    }

    protected async Task<IActionResult> ExecuteAsync(Func<Task<Result>> action, string? successMessage = null)
    {
        var result = await action();

        if (result.IsSuccess)
            return Ok(ApiResponse<object>.Ok(null, successMessage));

        _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);

        return MapFailure<object>(result);
    }

    private ObjectResult MapFailure<TData>(Result result)
    {
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
}