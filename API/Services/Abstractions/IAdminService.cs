namespace API.Services.Abstractions;

public interface IAdminService
{
    Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default);
    Task<Result<UserDto>> CreateUserAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<Result<UserDto>> UpdateUserAsync(int userId, UserDto dto, CancellationToken ct = default);
    Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default);
}