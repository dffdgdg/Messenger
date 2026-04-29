namespace MessengerAPI.Services.Abstractions;

public interface IUserStatusService
{
    Task<Result> SetStatusAsync(int userId, UserStatusType statusType, TimeSpan? duration);
    Task<Result<UserStatusDto>> GetStatusAsync(int userId);
    Task CleanupExpiredStatusesAsync();
}