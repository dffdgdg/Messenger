using MessengerShared.DTO;
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

        #region Success Responses

        protected ActionResult<TResponse> Success<TResponse>(TResponse data, string? message = null)
        {
            var response = new ApiResponse<TResponse>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            return Ok(response);
        }

        protected ActionResult Success(string? message = null)
        {
            var response = new ApiResponse<object>
            {
                Success = true,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            return Ok(response);
        }

        protected int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("Invalid user identity");
            return userId;
        }

        protected string GetCurrentUsername()
        {
            var usernameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
            return usernameClaim ?? throw new UnauthorizedAccessException("Username not found");
        }

        protected bool IsCurrentUser(int userId)
        {
            return GetCurrentUserId() == userId;
        }
        protected ActionResult SuccessWithData<E>(E data, string? message = null)
        {
            var response = new ApiResponse<E>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            return Ok(response);
        }
        protected ActionResult Created<TResponse>(TResponse data, string message = "Resource created successfully")
        {
            var response = new ApiResponse<TResponse>
            {
                Success = true,
                Data = data,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            return StatusCode(StatusCodes.Status201Created, response);
        }

        protected ActionResult NoContent(string message = "Resource updated successfully")
        {
            var response = new ApiResponse<object>
            {
                Success = true,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        #endregion

        #region Error Responses

        protected ActionResult BadRequest(string error, string? details = null)
        {
            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Details = details,
                Timestamp = DateTime.UtcNow
            };
            return BadRequest(response);
        }

        protected ActionResult NotFound(string error = "Resource not found")
        {
            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.UtcNow
            };
            return NotFound(response);
        }

        protected ActionResult Unauthorized(string error = "Unauthorized access")
        {
            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.UtcNow
            };
            return Unauthorized(response);
        }

        protected ActionResult Forbidden(string error = "Access forbidden")
        {
            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.UtcNow
            };
            return StatusCode(StatusCodes.Status403Forbidden, response);
        }

        protected ActionResult InternalError(Exception ex, string error = "An internal error occurred")
        {
            _logger.LogError(ex, "Internal server error");

            var response = new ApiResponse<object>
            {
                Success = false,
                Error = error,
                Timestamp = DateTime.UtcNow
            };

            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                response.Details = ex.ToString();
            }

            return StatusCode(StatusCodes.Status500InternalServerError, response);
        }

        #endregion

        #region Helper Methods

        protected async Task<ActionResult<ApiResponse<T>>> ExecuteAsync<T>(Func<Task<T>> action,string? successMessage = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> START: {successMessage}");

                var result = await action();

                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> - action result: {result != null}");
                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> - result type: {typeof(T)}");
                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> - result actual type: {result?.GetType()}");

                if (result is List<UserDTO> users)
                {
                    System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> - users count: {users.Count}");
                }

                var response = new ApiResponse<T>
                {
                    Success = true,
                    Data = result,
                    Message = successMessage,
                    Timestamp = DateTime.UtcNow
                };

                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> - response created: {response != null}");

                return Ok(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteAsync<T> EXCEPTION: {ex}");

                var errorResponse = new ApiResponse<T>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                return BadRequest(errorResponse);
            }
        }

        protected async Task<ActionResult> ExecuteAsync(Func<Task> action, string? successMessage = null)
        {
            try
            {
                await action();
                return Success(successMessage);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Bad request");
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found");
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return InternalError(ex);
            }
        }

        protected bool ValidateModel()
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToArray();

                throw new ArgumentException($"Invalid model: {string.Join(", ", errors)}");
            }
            return true;
        }

        #endregion
    }
}