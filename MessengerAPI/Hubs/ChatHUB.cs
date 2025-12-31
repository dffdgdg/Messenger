using MessengerAPI.Model;
using MessengerAPI.Services;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MessengerAPI.Hubs
{
    [Authorize]
    public class ChatHub(
        IServiceScopeFactory scopeFactory,
        IOnlineUserService onlineUserService,
        ILogger<ChatHub> logger) : Hub
    {
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        private readonly IOnlineUserService _onlineUserService = onlineUserService ?? throw new ArgumentNullException(nameof(onlineUserService));
        private readonly ILogger<ChatHub> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        #region Connection Lifecycle

        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                _onlineUserService.UserConnected(userId.Value, Context.ConnectionId);

                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

                var chatIds = await context.ChatMembers.Where(cm => cm.UserId == userId.Value).Select(cm => cm.ChatId).ToListAsync();

                foreach (var chatId in chatIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
                }

                await Clients.Others.SendAsync("UserOnline", userId.Value);

                _logger.LogInformation("Пользователь {UserId} подключился, чатов: {ChatCount}", userId.Value, chatIds.Count);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                _onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

                var stillOnline = _onlineUserService.IsUserOnline(userId.Value);

                if (!stillOnline)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

                    var user = await context.Users.FindAsync(userId.Value);
                    if (user != null)
                    {
                        user.LastOnline = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        await context.SaveChangesAsync();
                    }

                    await Clients.Others.SendAsync("UserOffline", userId.Value);

                    _logger.LogInformation("Пользователь {UserId} отключился", userId.Value);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        #endregion

        #region Chat Groups

        public async Task JoinChat(int chatId)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue) throw new UnauthorizedAccessException("User not authenticated");

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

            var isMember = await context.ChatMembers.AnyAsync(cm => cm.UserId == userId.Value && cm.ChatId == chatId);

            if (!isMember)
            {
                _logger.LogWarning("Попытка присоединиться к чату без доступа: UserId={UserId}, ChatId={ChatId}", userId.Value, chatId);
                throw new UnauthorizedAccessException($"User {userId.Value} is not a member of chat {chatId}");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            _logger.LogDebug("Пользователь {UserId} присоединился к чату {ChatId}", userId.Value, chatId);
        }

        public async Task LeaveChat(int chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            _logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}", Context.ConnectionId, chatId);
        }

        #endregion

        #region Read Receipts

        /// <summary>
        /// Получить информацию о прочтении для текущего пользователя в чате
        /// </summary>
        public async Task<ChatReadInfoDTO?> GetReadInfo(int chatId)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return null;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
                return await readReceiptService.GetChatReadInfoAsync(userId.Value, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка GetReadInfo для user={UserId}, chat={ChatId}", userId.Value, chatId);
                return null;
            }
        }

        /// <summary>
        /// Отметить все сообщения как прочитанные (до последнего или до указанного)
        /// </summary>
        public async Task MarkAsRead(int chatId, int? messageId = null)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

                var result = await readReceiptService.MarkAsReadAsync(userId.Value, new MarkAsReadDTO
                {
                    ChatId = chatId,
                    MessageId = messageId
                });

                if (result == null)
                {
                    _logger.LogWarning("MarkAsRead returned null for user {UserId}, chat {ChatId}",
                        userId.Value, chatId);
                    return;
                }

                // Отправляем обновлённый счётчик вызывающему
                await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, result.UnreadCount);

                // Уведомляем других участников чата о прочтении
                await Clients.OthersInGroup($"chat_{chatId}").SendAsync("MessageRead", chatId, userId.Value, result.LastReadMessageId, result.LastReadAt);

                _logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}", userId.Value, chatId, result.UnreadCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка MarkAsRead для пользователя {UserId} в чате {ChatId}", userId.Value, chatId);
            }
        }

        /// <summary>
        /// Отметить конкретное сообщение как прочитанное (вызывается при скролле)
        /// </summary>
        public async Task MarkMessageAsRead(int chatId, int messageId)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();

                var result = await readReceiptService.MarkMessageAsReadAsync(userId.Value, chatId, messageId);

                // Отправляем обновлённый счётчик вызывающему
                await Clients.Caller.SendAsync("UnreadCountUpdated", chatId, result.UnreadCount);

                // Уведомляем других участников чата о прочтении
                await Clients.OthersInGroup($"chat_{chatId}").SendAsync("MessageRead",chatId,userId.Value,result.LastReadMessageId,result.LastReadAt);

                _logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}",userId.Value, messageId, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка MarkMessageAsRead для user={UserId}, chat={ChatId}, msg={MessageId}", userId.Value, chatId, messageId);
            }
        }

        /// <summary>
        /// Получить все непрочитанные
        /// </summary>
        public async Task<AllUnreadCountsDTO> GetUnreadCounts()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return new AllUnreadCountsDTO { Chats = [], TotalUnread = 0 };

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
                return await readReceiptService.GetAllUnreadCountsAsync(userId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения непрочитанных для пользователя {UserId}", userId.Value);
                return new AllUnreadCountsDTO { Chats = [], TotalUnread = 0 };
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
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

            var memberIds = await context.ChatMembers.Where(cm => cm.ChatId == chatId).Select(cm => cm.UserId).ToListAsync();

            var onlineMembers = _onlineUserService.FilterOnlineUserIds(memberIds);
            return [.. onlineMembers];
        }

        #endregion

        #region Helpers

        private int? GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                _logger.LogWarning("Некорректный claim NameIdentifier для соединения {ConnectionId}",
                    Context.ConnectionId);
                return null;
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Некорректный claim NameIdentifier: {Claim}", userIdClaim);
                return null;
            }

            return userId;
        }

        #endregion
    }
}