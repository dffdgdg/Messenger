using MessengerAPI.Services.ReadReceipt;
using System.Security.Claims;

namespace MessengerAPI.Hubs;

[Authorize]
public sealed class ChatHub(
    IServiceScopeFactory scopeFactory,
    IOnlineUserService onlineUserService,
    AppDateTime appDateTime,
    ILogger<ChatHub> logger) : Hub
{
    #region Connection Lifecycle

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            await base.OnConnectedAsync();
            return;
        }

        onlineUserService.UserConnected(userId.Value, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

        using var scope = scopeFactory.CreateScope();
        var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();

        var chatIds = await accessControl.GetUserChatIdsAsync(userId.Value);

        var joinTasks = chatIds.Select(chatId => Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}"));
        await Task.WhenAll(joinTasks);

        await Clients.Others.SendAsync("UserOnline", userId.Value);

        logger.LogInformation("Пользователь {UserId} подключился, чатов: {ChatCount}",
            userId.Value, chatIds.Count);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

            if (!onlineUserService.IsOnline(userId.Value))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

                    await context.Users
                        .Where(u => u.Id == userId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastOnline, appDateTime.UtcNow));

                    await Clients.Others.SendAsync("UserOffline", userId.Value);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при обработке отключения {UserId}", userId.Value);
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
        var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();

        var result = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (result.IsFailure)
            throw new HubException(result.Error);

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

        using var scope = scopeFactory.CreateScope();
        var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

        var result = await readReceiptService.GetChatReadInfoAsync(userId.Value, chatId);
        return result.UnwrapOrDefault(logger);
    }

    public async Task MarkAsRead(int chatId, int? messageId = null)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

        var result = await readReceiptService.MarkAsReadAsync(
            userId.Value,
            new MarkAsReadDto { ChatId = chatId, MessageId = messageId });

        if (!result.TryUnwrap(out var receipt, logger))
            return;

        await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, receipt.UnreadCount);

        await Clients.OthersInGroup($"chat_{chatId}").SendAsync(
            "MessageRead", chatId, userId.Value,
            receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}",
            userId.Value, chatId, receipt.UnreadCount);
    }

    public async Task MarkMessageAsRead(int chatId, int messageId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

        var result = await readReceiptService.MarkMessageAsReadAsync(userId.Value, chatId, messageId);

        if (!result.TryUnwrap(out var receipt, logger))
            return;

        await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, receipt.UnreadCount);

        await Clients.OthersInGroup($"chat_{chatId}").SendAsync("MessageRead", chatId, userId.Value,
            receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}",
            userId.Value, messageId, chatId);
    }

    public async Task<AllUnreadCountsDto> GetUnreadCounts()
    {
        var userId = GetCurrentUserId();
        var fallback = new AllUnreadCountsDto { Chats = [], TotalUnread = 0 };

        if (!userId.HasValue)
            return fallback;

        using var scope = scopeFactory.CreateScope();
        var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

        var result = await readReceiptService.GetAllUnreadCountsAsync(userId.Value);
        return result.UnwrapOrFallback(fallback, logger);
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
        var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();

        var memberCheck = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (memberCheck.IsFailure)
            throw new HubException(memberCheck.Error);

        var memberIds = await accessControl.GetUserChatIdsAsync(chatId);
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

    private int GetRequiredUserId()
        => GetCurrentUserId() ?? throw new HubException("User not authenticated");

    #endregion
}