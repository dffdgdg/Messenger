namespace API.Services.Abstractions;

public interface IUserService
{
    Task<Result<List<UserDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<Result<UserDto>> GetUserAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateUserAsync(int id, UserDto dto, CancellationToken ct = default);
    Task<Result<AvatarResponseDto>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default);
    Task<Result> RemoveAvatarAsync(int id, CancellationToken ct = default);
    Task<Result<OnlineUsersResponseDto>> GetOnlineUsersAsync(CancellationToken ct = default);
    Task<Result<UserStatusDto>> GetOnlineStatusAsync(int userId, CancellationToken ct = default);
    Task<Result<List<UserStatusDto>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default);
    Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDto dto, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int id, ChangePasswordDto dto, CancellationToken ct = default);
}