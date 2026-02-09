using MessengerAPI.Common;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<T>(ILogger<T> logger) : ControllerBase where T : BaseController<T>
{
    protected readonly ILogger<T> _logger = logger;

    #region User Identity

    protected int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Недействительная идентификация пользователя");
        return userId;
    }

    protected string GetCurrentUsername()
    {
        var usernameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
        return usernameClaim ?? throw new UnauthorizedAccessException("Имя пользователя не найдено");
    }

    protected bool IsCurrentUser(int userId) => GetCurrentUserId() == userId;

    #endregion

    #region Result<T> Responses

    protected ActionResult<ApiResponse<TResult>> FromResult<TResult>(Result<TResult> result, string? successMessage = null)
    {
        if (result.IsSuccess)
        {
            return Ok(new ApiResponse<TResult>
            {
                Success = true,
                Data = result.Value,
                Message = successMessage,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogWarning("Бизнес-ошибка: {Error}", result.Error);
        return BadRequest(new ApiResponse<TResult>
        {
            Success = false,
            Error = result.Error,
            Timestamp = DateTime.UtcNow
        });
    }

    protected ActionResult FromResult(Result result, string? successMessage = null)
    {
        if (result.IsSuccess)
        {
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = successMessage,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogWarning("Бизнес-ошибка: {Error}", result.Error);
        return BadRequest(new ApiResponse<object>
        {
            Success = false,
            Error = result.Error,
            Timestamp = DateTime.UtcNow
        });
    }

    protected async Task<ActionResult<ApiResponse<TResult>>> ExecuteResultAsync<TResult>(Func<Task<Result<TResult>>> action, string? successMessage = null)
    {
        try
        {
            var result = await action();
            return FromResult(result, successMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized(new ApiResponse<TResult>
            {
                Success = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return InternalError<TResult>(ex);
        }
    }

    protected async Task<IActionResult> ExecuteResultAsync(
        Func<Task<Result>> action, string? successMessage = null)
    {
        try
        {
            var result = await action();
            return FromResult(result, successMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    #endregion

    #region Legacy ExecuteAsync (для постепенной миграции — удалить после полного перехода)

    protected async Task<ActionResult<ApiResponse<TResult>>> ExecuteAsync<TResult>(
        Func<Task<TResult>> action, string? successMessage = null)
    {
        try
        {
            var result = await action();
            return Ok(new ApiResponse<TResult>
            {
                Success = true,
                Data = result,
                Message = successMessage,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации");
            return BadRequest<TResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized<TResult>(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Ресурс не найден");
            return NotFound<TResult>(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Недопустимая операция");
            return BadRequest<TResult>(ex.Message);
        }
        catch (Exception ex)
        {
            return InternalError<TResult>(ex);
        }
    }

    protected async Task<IActionResult> ExecuteAsync(
        Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = successMessage,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации");
            return BadRequestMessage(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неавторизованный доступ");
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Ресурс не найден");
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Недопустимая операция");
            return BadRequestMessage(ex.Message);
        }
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    #endregion

    #region Success Responses

    protected ActionResult<ApiResponse<TData>> Success<TData>(
        TData data, string? message = null)
        => Ok(new ApiResponse<TData>
        {
            Success = true,
            Data = data,
            Message = message,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult<ApiResponse<object>> Success(string? message = null)
        => Ok(new ApiResponse<object>
        {
            Success = true,
            Message = message,
            Timestamp = DateTime.UtcNow
        });

    #endregion

    #region Error Responses

    protected ActionResult<ApiResponse<TData>> BadRequest<TData>(
        string error, string? details = null)
        => BadRequest(new ApiResponse<TData>
        {
            Success = false,
            Error = error,
            Details = details,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult BadRequestMessage(
        string error, string? details = null)
        => BadRequest(new ApiResponse<object>
        {
            Success = false,
            Error = error,
            Details = details,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult<ApiResponse<TData>> NotFound<TData>(
        string error = "Ресурс не найден")
        => NotFound(new ApiResponse<TData>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult<ApiResponse<TData>> Unauthorized<TData>(
        string error = "Неавторизованный доступ")
        => Unauthorized(new ApiResponse<TData>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult<ApiResponse<TData>> Forbidden<TData>(
        string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<TData>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult Forbidden(string error = "Доступ запрещён")
        => StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<object>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        });

    protected ActionResult InternalError(Exception ex,
        string error = "Произошла внутренняя ошибка")
    {
        _logger.LogError(ex, "Внутренняя ошибка: {Message}", ex.Message);

        var response = new ApiResponse<object>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        };

        var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        if (env?.IsDevelopment() == true)
            response.Details = ex.ToString();

        return StatusCode(StatusCodes.Status500InternalServerError, response);
    }

    protected ActionResult<ApiResponse<TData>> InternalError<TData>(
        Exception ex, string error = "Произошла внутренняя ошибка")
    {
        _logger.LogError(ex, "Внутренняя ошибка: {Message}", ex.Message);

        var response = new ApiResponse<TData>
        {
            Success = false,
            Error = error,
            Timestamp = DateTime.UtcNow
        };

        var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        if (env?.IsDevelopment() == true)
            response.Details = ex.ToString();

        return StatusCode(StatusCodes.Status500InternalServerError, response);
    }

    #endregion

    #region Validation

    protected void ValidateModel()
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToArray();
            throw new ArgumentException(
                $"Некорректная модель: {string.Join(", ", errors)}");
        }
    }

    #endregion
}