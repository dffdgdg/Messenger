using MessengerAPI.Services.ReadReceipt;
using System.Security.Claims;

namespace MessengerAPI.Hubs;

[Authorize]
public sealed class ChatHub(IServiceScopeFactory scopeFactory, IOnlineUserService onlineUserService,
    ILogger<ChatHub> logger) : Hub
{
    #region Connection Lifecycle

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            onlineUserService.UserConnected(userId.Value, Context.ConnectionId);

            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

            var chatIds = await context.ChatMembers
                .Where(cm => cm.UserId == userId.Value)
                .Select(cm => cm.ChatId).ToListAsync();

            var joinTasks = chatIds.Select(chatId =>
                Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}"));
            await Task.WhenAll(joinTasks);

            await Clients.Others.SendAsync("UserOnline", userId.Value);

            logger.LogInformation("Пользователь {UserId} подключился, чатов: {ChatCount}",
                userId.Value, chatIds.Count);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

            var stillOnline = onlineUserService.IsOnline(userId.Value);

            if (!stillOnline)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider
                        .GetRequiredService<MessengerDbContext>();

                    var user = await context.Users.FindAsync(userId.Value);
                    if (user != null)
                    {
                        user.LastOnline = AppDateTime.UtcNow;
                        await context.SaveChangesAsync();
                    }

                    await Clients.Others.SendAsync("UserOffline", userId.Value);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при обработке отключения {UserId}",
                        userId.Value);
                }

                logger.LogInformation("Пользователь {UserId} отключился", userId.Value);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Chat Groups

    public async Task JoinChat(int chatId)
    {
        var userId = GetRequiredUserId();

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

        var isMember = await context.ChatMembers.AnyAsync(cm =>
            cm.UserId == userId && cm.ChatId == chatId);

        if (!isMember)
        {
            logger.LogWarning("Попытка присоединиться к чату без доступа: UserId={UserId}, ChatId={ChatId}",
                userId, chatId);
            throw new HubException($"User {userId} is not a member of chat {chatId}");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        logger.LogDebug("Пользователь {UserId} присоединился к чату {ChatId}", userId, chatId);
    }

    public async Task LeaveChat(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}",
            Context.ConnectionId, chatId);
    }

    #endregion

    #region Read Receipts

    public async Task<ChatReadInfoDto?> GetReadInfo(int chatId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return null;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var readReceiptService = scope.ServiceProvider
                .GetRequiredService<IReadReceiptService>();
            return await readReceiptService
                .GetChatReadInfoAsync(userId.Value, chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Ошибка GetReadInfo для user={UserId}, chat={ChatId}",
                userId.Value, chatId);
            return null;
        }
    }

    public async Task MarkAsRead(int chatId, int? messageId = null)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var readReceiptService = scope.ServiceProvider
                .GetRequiredService<IReadReceiptService>();

            var result = await readReceiptService.MarkAsReadAsync(
                userId.Value,
                new MarkAsReadDto
                {
                    ChatId = chatId,
                    MessageId = messageId
                });

            await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, result.UnreadCount);

            await Clients.OthersInGroup($"chat_{chatId}").SendAsync(
                "MessageRead",
                chatId,
                userId.Value,
                result.LastReadMessageId,
                result.LastReadAt);

            logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}",
                userId.Value, chatId, result.UnreadCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Ошибка MarkAsRead для пользователя {UserId} в чате {ChatId}",
                userId.Value, chatId);
        }
    }

    public async Task MarkMessageAsRead(int chatId, int messageId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var readReceiptService = scope.ServiceProvider
                .GetRequiredService<IReadReceiptService>();

            var result = await readReceiptService
                .MarkMessageAsReadAsync(userId.Value, chatId, messageId);

            await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, result.UnreadCount);

            await Clients.OthersInGroup($"chat_{chatId}").SendAsync(
                "MessageRead",
                chatId,
                userId.Value,
                result.LastReadMessageId,
                result.LastReadAt);

            logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}",
                userId.Value, messageId, chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка MarkMessageAsRead для user={UserId}, chat={ChatId}, msg={MessageId}",
                userId.Value, chatId, messageId);
        }
    }

    public async Task<AllUnreadCountsDto> GetUnreadCounts()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return new AllUnreadCountsDto { Chats = [], TotalUnread = 0 };

        try
        {
            using var scope = scopeFactory.CreateScope();
            var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
            return await readReceiptService.GetAllUnreadCountsAsync(userId.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка получения непрочитанных для пользователя {UserId}", userId.Value);
            return new AllUnreadCountsDto { Chats = [], TotalUnread = 0 };
        }
    }

    #endregion

    #region Typing & Online

    public async Task SendTyping(int chatId)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            await Clients.OthersInGroup($"chat_{chatId}").SendAsync("UserTyping", chatId, userId.Value);
        }
    }

    public async Task<List<int>> GetOnlineUsersInChat(int chatId)
    {
        var userId = GetRequiredUserId();

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

        var isMember = await context.ChatMembers.AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

        if (!isMember)
            throw new HubException("Нет доступа к этому чату");

        var memberIds = await context.ChatMembers.Where(cm => cm.ChatId == chatId)
            .Select(cm => cm.UserId).ToListAsync();

        return [.. onlineUserService.FilterOnline(memberIds)];
    }

    #endregion

    #region Helpers

    private int? GetCurrentUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return null;

        return userId;
    }

    private int GetRequiredUserId() => GetCurrentUserId() ?? throw new HubException("User not authenticated");

    #endregion
}