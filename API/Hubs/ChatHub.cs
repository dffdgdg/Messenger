using API.Services.Infrastructure.Security;
using Shared.Hubs;
using System.Security.Claims;

namespace API.Hubs;

[Authorize]
public sealed partial class ChatHub(IServiceScopeFactory scopeFactory, IOnlineUserService onlineUserService, AppDateTime appDateTime, ILogger<ChatHub> logger) : Hub
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
        var statusService = scope.ServiceProvider.GetRequiredService<IUserStatusService>();

        var chatIds = await accessControl.GetUserChatIdsAsync(userId.Value);
        await Task.WhenAll(chatIds.Select(id => Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{id}")));

        var statusResult = await statusService.GetStatusAsync(userId.Value);
        if (statusResult.TryUnwrap(out var statusDto, logger))
        {
            await Clients.Caller.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
            await Clients.Others.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
        }
        else
        {
            await Clients.Others.SendAsync(HubMethods.Chat.UserOnline, userId.Value);
        }

        LogUserConnected(userId.Value, chatIds.Count);
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

                    await context.Users.Where(u => u.Id == userId.Value).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastOnline, appDateTime.UtcNow));

                    await Clients.Others.SendAsync(HubMethods.Chat.UserOffline, userId.Value);
                }
                catch (Exception ex)
                {
                    if (logger.IsEnabled(LogLevel.Error))
                    {
                        logger.LogError(ex, "Ошибка при обработке отключения {UserId}", userId.Value);
                    }
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Пользователь {UserId} отключился", userId.Value);
                }
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

        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure)
            throw new HubException(access.Error);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Пользователь {UserId} присоединился к чату {ChatId}", userId, chatId);
        }
    }

    public async Task LeaveChat(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}", Context.ConnectionId, chatId);
        }
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

        var result = await readReceiptService.MarkAsReadAsync(userId.Value, new MarkAsReadDto { ChatId = chatId, MessageId = messageId });

        if (!result.TryUnwrap(out var receipt, logger))
            return;

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);
        await Clients.OthersInGroup($"chat_{chatId}")
            .SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}", userId.Value, chatId, receipt.UnreadCount);
        }
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

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);
        await Clients.OthersInGroup($"chat_{chatId}")
            .SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}", userId.Value, messageId, chatId);
        }
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

    #region Typing and Online

    public async Task SendTyping(int chatId)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            await Clients.OthersInGroup($"chat_{chatId}").SendAsync(HubMethods.Chat.UserTyping, chatId, userId.Value);
        }
    }

    public async Task<List<int>> GetOnlineUsersInChat(int chatId)
    {
        var userId = GetRequiredUserId();

        using var scope = scopeFactory.CreateScope();
        var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();

        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure)
            throw new HubException(access.Error);

        return [.. onlineUserService.FilterOnline(await accessControl.GetUserChatIdsAsync(chatId))];
    }

    public async Task SetStatus(int statusRaw, string? duration = null)
    {
        var userId = GetRequiredUserId();

        if (!System.Enum.IsDefined(typeof(UserStatusType), statusRaw))
        {
            logger.LogWarning("[SetStatus] Invalid statusRaw={StatusRaw} from userId={UserId}", statusRaw, userId);
            throw new HubException($"Неверный статус: {statusRaw}");
        }

        var userStatus = (UserStatusType)statusRaw;
        var parsedDuration = duration.Parse();

        using var scope = scopeFactory.CreateScope();
        var statusService = scope.ServiceProvider.GetRequiredService<IUserStatusService>();
        var result = await statusService.SetStatusAsync(userId, userStatus, parsedDuration);
        if (result.IsFailure)
            throw new HubException(result.Error);
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
        => GetCurrentUserId() ?? throw new HubException("Пользователь не аутентифицирован");

    #endregion

    #region Log
    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} подключился, чатов: {ChatCount}")]
    private partial void LogUserConnected(int userId, int chatCount);
    #endregion
}