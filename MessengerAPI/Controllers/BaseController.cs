using MessengerAPI.Common;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;
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

    protected async Task<ActionResult<ApiResponse<TResult>>> ExecuteAsync<TResult>(
        Func<Task<Result<TResult>>> action, string? successMessage = null)
    {
        try
        {
            var result = await action();

            if (result.IsSuccess)
                return Ok(ApiResponse<TResult>.Ok(result.Value!, successMessage));

            _logger.LogWarning("Бизнес-ошибка: {Error}", result.Error);
            return BadRequest(ApiResponse<TResult>.Fail(result.Error!));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized(ApiResponse<TResult>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Ресурс не найден");
            return NotFound(ApiResponse<TResult>.Fail(ex.Message));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации");
            return BadRequest(ApiResponse<TResult>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Недопустимая операция");
            return BadRequest(ApiResponse<TResult>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return HandleInternalError<TResult>(ex);
        }
    }

    protected async Task<IActionResult> ExecuteAsync(
        Func<Task<Result>> action, string? successMessage = null)
    {
        try
        {
            var result = await action();

            if (result.IsSuccess)
                return Ok(ApiResponse<object>.Ok(null, successMessage));

            _logger.LogWarning("Бизнес-ошибка: {Error}", result.Error);
            return BadRequest(ApiResponse<object>.Fail(result.Error!));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized(ApiResponse<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Ресурс не найден");
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации");
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Недопустимая операция");
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return HandleInternalError(ex);
        }
    }

    protected ActionResult Forbidden(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(error));

    protected ActionResult<ApiResponse<TData>> Forbidden<TData>(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TData>.Fail(error));

    private ActionResult<ApiResponse<TData>> HandleInternalError<TData>(
        Exception ex, string error = "Произошла внутренняя ошибка")
    {
        _logger.LogError(ex, "Внутренняя ошибка: {Message}", ex.Message);
        return StatusCode(500, ApiResponse<TData>.Fail(error));
    }

    private ObjectResult HandleInternalError(Exception ex, string error = "Произошла внутренняя ошибка")
    {
        _logger.LogError(ex, "Внутренняя ошибка: {Message}", ex.Message);
        return StatusCode(500, ApiResponse<object>.Fail(error));
    }
}