using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Dto.Online;
using Shared.Enum;
using Shared.Hubs;

namespace API.Infrastructure.Status;

public sealed partial class UserStatusService(
    IUserRepository userRepository,
    IDepartmentRepository departmentRepository,
    IOnlineUserService onlineUserService,
    IHubNotifier hubNotifier,
    TimeProvider timeProvider,
    ILogger<UserStatusService> logger) : IUserStatusService
{
    public async Task<Result> SetStatusAsync(int userId, UserStatusType statusType, TimeSpan? duration)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = duration.HasValue ? DateTime.SpecifyKind(now.Add(duration.Value), DateTimeKind.Unspecified)
            : (DateTime?)null;

        var user = await userRepository.FindByIdTrackedAsync(userId);
        if (user is null)
            return Result.NotFound($"Пользователь {userId} не найден");

        user.StatusType = statusType;
        user.StatusExpiresAt = expiresAt;

        var dto = new UserStatusDto(user.Id, onlineUserService.IsOnline(user.Id), user.LastOnline, statusType, expiresAt);
        await hubNotifier.SendToChatAsync(0, HubMethods.Chat.UserStatusChanged, dto);

        LogStatusSet(userId, statusType, duration);
        return Result.Success();
    }

    public async Task<Result<UserStatusDto>> GetStatusAsync(int userId)
    {
        var user = await userRepository.GetWithSettingsAsync(userId);

        if (user is null)
            return Result<UserStatusDto>.NotFound($"Пользователь {userId} не найден");

        var isOnline = onlineUserService.IsOnline(user.Id);

        return Result<UserStatusDto>.Success(new UserStatusDto(user.Id, isOnline, user.LastOnline,
            isOnline ? user.StatusType : UserStatusType.Online, user.StatusExpiresAt));
    }

    public async Task CleanupExpiredStatusesAsync()
    {
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

        var expiredUsers = await userRepository.GetExpiredStatusUsersAsync(now);

        if (expiredUsers.Count == 0)
            return;

        foreach (var user in expiredUsers)
        {
            user.StatusType = UserStatusType.Online;
            user.StatusExpiresAt = null;

            var dto = new UserStatusDto(user.Id, onlineUserService.IsOnline(user.Id), user.LastOnline, UserStatusType.Online, null);
            await hubNotifier.SendToChatAsync(0, HubMethods.Chat.UserStatusChanged, dto);
        }

        LogStatusesCleanedUp(expiredUsers.Count);
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} set status {Status} duration {Duration}")]
    private partial void LogStatusSet(int userId, UserStatusType status, TimeSpan? duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired statuses")]
    private partial void LogStatusesCleanedUp(int count);

    #endregion
}