using MessengerAPI.Model;
using MessengerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MessengerAPI.Hubs
{
    [Authorize]
    public class ChatHub(IServiceScopeFactory scopeFactory, IOnlineUserService onlineUserService, ILogger<ChatHub> logger) : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                onlineUserService.UserConnected(userId.Value, Context.ConnectionId);

                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

                var chatIds = await context.ChatMembers.Where(cm => cm.UserId == userId.Value).Select(cm => cm.ChatId).ToListAsync();

                foreach (var chatId in chatIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
                }

                await Clients.Others.SendAsync("UserOnline", new
                {
                    UserId = userId.Value,
                    IsOnline = true,
                    Timestamp = DateTime.Now
                });

                logger.LogInformation("Пользователь {UserId} Подключился. ConnectionId: {ConnectionId}",
                    userId.Value, Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);

                var stillOnline = onlineUserService.IsUserOnline(userId.Value);

                logger.LogInformation("Пользователь {UserId} Отключился. ConnectionId: {ConnectionId}. Ещё онлайн: {StillOnline}", 
                    userId.Value, Context.ConnectionId, stillOnline);

                if (!stillOnline)
                {
                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

                    try
                    {
                        var user = await context.Users.FindAsync(userId.Value);
                        if (user != null)
                        {
                            user.LastOnline = DateTime.Now;
                            var saved = await context.SaveChangesAsync();

                            logger.LogInformation("Обновлено время последнего онлайна пользователя {UserId}. Затронуто строк: {Rows}", userId.Value, saved);
                        }
                        else
                        {
                            logger.LogWarning("Пользователь {UserId} не найден в базе данных", userId.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Ошибка обновления онлайна пользователя {UserId}", userId.Value);
                    }

                    await Clients.Others.SendAsync("UserOffline", new
                    {
                        UserId = userId.Value,
                        IsOnline = false,
                        LastOnline = DateTime.Now
                    });
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
            logger.LogDebug("Соединение {ConnectionId} присоединилось к чату {ChatId}",
                Context.ConnectionId, chatId);
        }

        public async Task LeaveChat(int chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
            logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}",
                Context.ConnectionId, chatId);
        }

        public async Task SendTyping(int chatId)
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                await Clients.OthersInGroup(chatId.ToString()).SendAsync("UserTyping", new { ChatId = chatId, UserId = userId.Value });
            }
        }

        public async Task<List<int>> GetOnlineUsersInChat(int chatId)
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

            var memberIds = await context.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var onlineMembers = onlineUserService.FilterOnlineUserIds(memberIds);
            return [.. onlineMembers];
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                logger.LogWarning("Некорректный claim NameIdentifier найден для соединения {ConnectionId}", Context.ConnectionId);
                return null;
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                logger.LogWarning("Некорректный claim NameIdentifier: {Claim}", userIdClaim);
                return null;
            }

            return userId;
        }
    }
}