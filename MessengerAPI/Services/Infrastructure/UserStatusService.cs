using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.Dto.Online;
using MessengerShared.Enum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services.Infrastructure;

public interface IUserStatusService
{
    Task SetStatusAsync(int userId, UserStatusType status, TimeSpan? duration = null);
    Task<UserStatusDto> GetStatusAsync(int userId);
    Task CleanupExpiredStatusesAsync();
}

public class UserStatusService(
    MessengerDbContext db,
    IOnlineUserService onlineUserService,
    IHubContext<ChatHub> hubContext,
    ILogger<UserStatusService> logger) : IUserStatusService
{
    public async Task SetStatusAsync(int userId, UserStatusType status, TimeSpan? duration = null)
    {
        var user = await db.Users.FindAsync(userId);
        if (user == null) return;

        user.StatusType = status;
        user.StatusExpiresAt = duration.HasValue
            ? DateTime.SpecifyKind(DateTime.UtcNow.Add(duration.Value), DateTimeKind.Unspecified)
            : null;
        await db.SaveChangesAsync();

        var dto = await BuildDtoAsync(user);
        await hubContext.Clients.All.SendAsync("UserStatusChanged", dto);

        logger.LogInformation("User {UserId} set status {Status} for {Duration}", userId, status, duration);
    }

    public async Task<UserStatusDto> GetStatusAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return new UserStatusDto(userId, false, null);

        return await BuildDtoAsync(user);
    }

    public async Task CleanupExpiredStatusesAsync()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var expired = await db.Users
            .Where(u => u.StatusExpiresAt != null && u.StatusExpiresAt <= now)
            .ToListAsync();

        if (expired.Count == 0) return;

        foreach (var u in expired)
        {
            u.StatusType = UserStatusType.Online;
            u.StatusExpiresAt = null;
        }

        await db.SaveChangesAsync();

        foreach (var u in expired)
        {
            var dto = await BuildDtoAsync(u);
            await hubContext.Clients.All.SendAsync("UserStatusChanged", dto);
        }

        logger.LogInformation("Cleaned up {Count} expired statuses", expired.Count);
    }

    private Task<UserStatusDto> BuildDtoAsync(Model.User user)
    {
        var isOnline = onlineUserService.IsOnline(user.Id);
        return Task.FromResult(new UserStatusDto(
            user.Id,
            isOnline,
            user.LastOnline,
            isOnline ? user.StatusType : UserStatusType.Online,
            user.StatusExpiresAt
        ));
    }
}