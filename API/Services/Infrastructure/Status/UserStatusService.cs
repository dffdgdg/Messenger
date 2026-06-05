using API.Hubs;
using Shared.Hubs;

namespace API.Services.Infrastructure;

public sealed partial class UserStatusService(MessengerDbContext db, IOnlineUserService onlineUserService,
    IHubContext<MessengerHub> hubContext, TimeProvider timeProvider,
    ILogger<UserStatusService> logger) : IUserStatusService
{
    public async Task<Result> SetStatusAsync(int userId, UserStatusType statusType, TimeSpan? duration)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = duration.HasValue ? DateTime.SpecifyKind(now.Add(duration.Value), DateTimeKind.Unspecified)
            : (DateTime?)null;

        var updated = await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.StatusType, statusType)
            .SetProperty(u => u.StatusExpiresAt, expiresAt));

        if (updated == 0)
            return Result.NotFound($"Пользователь {userId} не найден");

        var user = await db.Users.AsNoTracking().Select(u => new { u.Id, u.LastOnline, u.StatusType, u.StatusExpiresAt })
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is not null)
        {
            var dto = new UserStatusDto(user.Id, onlineUserService.IsOnline(user.Id), user.LastOnline, statusType, expiresAt);
            await hubContext.Clients.All.SendAsync(HubMethods.Chat.UserStatusChanged, dto);
        }

        LogStatusSet(userId, statusType, duration);
        return Result.Success();
    }

    public async Task<Result<UserStatusDto>> GetStatusAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().Select(u => new { u.Id, u.LastOnline, u.StatusType, u.StatusExpiresAt })
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Result<UserStatusDto>.NotFound($"Пользователь {userId} не найден");

        var isOnline = onlineUserService.IsOnline(user.Id);

        return Result<UserStatusDto>.Success(new UserStatusDto(user.Id, isOnline, user.LastOnline, isOnline ? user.StatusType
            : UserStatusType.Online, user.StatusExpiresAt));
    }


    public async Task CleanupExpiredStatusesAsync()
    {
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        var expiredUsers = await db.Users.AsNoTracking()
            .Where(u => u.StatusExpiresAt != null && u.StatusExpiresAt <= now)
            .Select(u => new { u.Id, u.LastOnline })
            .ToListAsync();

        if (expiredUsers.Count == 0)
            return;

        var expiredUserIds = expiredUsers.Select(u => u.Id).ToList();
        var count = await db.Users.Where(u => expiredUserIds.Contains(u.Id) && u.StatusExpiresAt != null && u.StatusExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.StatusType, UserStatusType.Online).SetProperty(u => u.StatusExpiresAt, (DateTime?)null));

        if (count == 0)
            return;

        foreach (var user in expiredUsers)
        {
            var dto = new UserStatusDto(user.Id, onlineUserService.IsOnline(user.Id), user.LastOnline, UserStatusType.Online, null);
            await hubContext.Clients.All.SendAsync(HubMethods.Chat.UserStatusChanged, dto);
        }

        LogStatusesCleanedUp(count);
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} set status {Status} duration {Duration}")]
    private partial void LogStatusSet(int userId, UserStatusType status, TimeSpan? duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired statuses")]
    private partial void LogStatusesCleanedUp(int count);

    #endregion
}