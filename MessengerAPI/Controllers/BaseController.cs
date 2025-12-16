using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MessengerAPI.Controllers
{
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

        #region Success Responses

        protected ActionResult SuccessWithData<TData>(TData data, string? message = null)
        {
            return Ok(new ApiResponse<TData>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<TData>> Success<TData>(TData data, string? message = null)
        {
            return Ok(new ApiResponse<TData>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<object>> Success(string? message = null)
        {
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<TData>> Created<TData>(TData data, string message = "Ресурс успешно создан")
        {
            return StatusCode(StatusCodes.Status201Created, new ApiResponse<TData>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        #endregion

        #region Error Responses

        protected ActionResult<ApiResponse<TData>> BadRequest<TData>(string error, string? details = null)
        {
            return BadRequest(new ApiResponse<TData>
            {
                Success = false,
                Error = error,
                Details = details,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult BadRequestMessage(string error, string? details = null)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Details = details,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<TData>> NotFound<TData>(string error = "Ресурс не найден")
        {
            return NotFound(new ApiResponse<TData>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<TData>> Unauthorized<TData>(string error = "Неавторизованный доступ")
        {
            return Unauthorized(new ApiResponse<TData>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult<ApiResponse<TData>> Forbidden<TData>(string error = "Доступ запрещён")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<TData>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult Forbidden(string error = "Доступ запрещён")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            });
        }

        protected ActionResult InternalError(Exception ex, string error = "Произошла внутренняя ошибка")
        {
            _logger.LogError(ex, "Внутренняя ошибка сервера: {Message}", ex.Message);

            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            };

            var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            if (env?.IsDevelopment() == true)
            {
                response.Details = ex.ToString();
            }

            return StatusCode(StatusCodes.Status500InternalServerError, response);
        }

        protected ActionResult<ApiResponse<TData>> InternalError<TData>(Exception ex, string error = "Произошла внутренняя ошибка")
        {
            _logger.LogError(ex, "Внутренняя ошибка сервера: {Message}", ex.Message);

            var response = new ApiResponse<TData>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.Now
            };

            var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            if (env?.IsDevelopment() == true)
            {
                response.Details = ex.ToString();
            }

            return StatusCode(StatusCodes.Status500InternalServerError, response);
        }

        #endregion

        #region Execute Helpers

        protected async Task<ActionResult<ApiResponse<TResult>>> ExecuteAsync<TResult>(Func<Task<TResult>> action, string? successMessage = null)
        {
            try
            {
                var result = await action();

                return Ok(new ApiResponse<TResult>
                {
                    Success = true,
                    Data = result,
                    Message = successMessage,
                    Timestamp = DateTime.Now
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации");
                return BadRequest<TResult>(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Попытка неавторизованного доступа");
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

        protected async Task<IActionResult> ExecuteAsync(Func<Task> action, string? successMessage = null)
        {
            try
            {
                await action();
                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = successMessage,
                    Timestamp = DateTime.Now
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации");
                return BadRequestMessage(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Попытка неавторизованного доступа");
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.Now
                });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Ресурс не найден");
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.Now
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

        #region Validation

        protected void ValidateModel()
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToArray();
                throw new ArgumentException($"Некорректная модель: {string.Join(", ", errors)}");
            }
        }

        #endregion
    }
}