using MessengerAPI.Hubs;

namespace MessengerAPI.Services.Infrastructure;

public sealed partial class UserStatusService(MessengerDbContext db, IOnlineUserService onlineUserService, IHubContext<ChatHub> hubContext, TimeProvider timeProvider,
    ILogger<UserStatusService> logger) : IUserStatusService
{
    public async Task<Result> SetStatusAsync(int userId, UserStatusType status, TimeSpan? duration = null)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return Result.NotFound($"Пользователь {userId} не найден");

        var now = timeProvider.GetUtcNow().UtcDateTime;

        user.StatusType = status;
        user.StatusExpiresAt = duration.HasValue ? DateTime.SpecifyKind(now.Add(duration.Value), DateTimeKind.Unspecified) : null;

        await db.SaveChangesAsync();

        var dto = BuildDto(user);
        await hubContext.Clients.All.SendAsync("UserStatusChanged", dto);

        LogStatusSet(userId, status, duration);

        return Result.Success();
    }

    public async Task<Result<UserStatusDto>> GetStatusAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Result<UserStatusDto>.NotFound($"Пользователь {userId} не найден");

        return Result<UserStatusDto>.Success(BuildDto(user));
    }

    public async Task CleanupExpiredStatusesAsync()
    {
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

        var expired = await db.Users.Where(u => u.StatusExpiresAt != null && u.StatusExpiresAt <= now).ToListAsync();

        if (expired.Count == 0) return;

        foreach (var u in expired)
        {
            u.StatusType = UserStatusType.Online;
            u.StatusExpiresAt = null;
        }

        await db.SaveChangesAsync();

        var dtos = expired.ConvertAll(BuildDto);
        await Task.WhenAll(dtos.Select(dto => hubContext.Clients.All.SendAsync("UserStatusChanged", dto)));

        LogStatusesCleanedUp(expired.Count);
    }

    private UserStatusDto BuildDto(Data.User user)
    {
        var isOnline = onlineUserService.IsOnline(user.Id);

        return new UserStatusDto(user.Id, isOnline, user.LastOnline, isOnline ? user.StatusType : UserStatusType.Online,
            user.StatusExpiresAt
        );
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} set status {Status} duration {Duration}")]
    private partial void LogStatusSet(int userId, UserStatusType status, TimeSpan? duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired statuses")]
    private partial void LogStatusesCleanedUp(int count);

    #endregion
}