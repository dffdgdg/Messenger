using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IReadReceiptService
    {
        Task<ReadReceiptResponseDTO> MarkAsReadAsync(int userId, MarkAsReadDTO request);
        Task<int> GetUnreadCountAsync(int userId, int chatId);
        Task<AllUnreadCountsDTO> GetAllUnreadCountsAsync(int userId);
        Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds);
        Task MarkAllAsReadAsync(int userId, int chatId);
        Task<ChatReadInfoDTO?> GetChatReadInfoAsync(int userId, int chatId);
        Task<ReadReceiptResponseDTO> MarkMessageAsReadAsync(int userId, int chatId, int messageId);
    }

    public class ReadReceiptService(MessengerDbContext context, ILogger<ReadReceiptService> logger) : IReadReceiptService
    {
        public async Task<ReadReceiptResponseDTO> MarkAsReadAsync(int userId, MarkAsReadDTO request)
        {
            var member = await context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == request.ChatId && cm.UserId == userId)
                ?? throw new KeyNotFoundException($"Пользователь {userId} не является участником чата {request.ChatId}");

            var targetMessageId = await DetermineTargetMessageIdAsync(request);

            if (targetMessageId == 0)
            {
                return CreateResponse(member, 0);
            }

            if (!member.LastReadMessageId.HasValue || targetMessageId > member.LastReadMessageId.Value)
            {
                member.LastReadMessageId = targetMessageId;
                member.LastReadAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                logger.LogDebug("Пользователь {UserId} прочитал до сообщения {MessageId} в чате {ChatId}", userId, targetMessageId, request.ChatId);
            }

            var unreadCount = await GetUnreadCountAsync(userId, request.ChatId);
            return CreateResponse(member, unreadCount);
        }

        /// <summary>
        /// Отметить конкретное сообщение как прочитанное (при скролле)
        /// </summary>
        public async Task<ReadReceiptResponseDTO> MarkMessageAsReadAsync(int userId, int chatId, int messageId)
        {
            var member = await context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

            if (member == null)
            {
                return new ReadReceiptResponseDTO
                {
                    ChatId = chatId,
                    UnreadCount = 0
                };
            }

            // Обновляем только если это сообщение "дальше" текущего прочитанного
            if (!member.LastReadMessageId.HasValue || messageId > member.LastReadMessageId.Value)
            {
                // Проверяем что сообщение существует
                var messageExists = await context.Messages.AnyAsync(m => m.Id == messageId && m.ChatId == chatId && m.IsDeleted != true);

                if (messageExists)
                {
                    member.LastReadMessageId = messageId;
                    member.LastReadAt = DateTime.Now;
                    await context.SaveChangesAsync();

                    logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}", userId, messageId, chatId);
                }
            }

            var unreadCount = await GetUnreadCountAsync(userId, chatId);
            return CreateResponse(member, unreadCount);
        }

        /// <summary>
        /// Получить информацию о прочтении для пользователя в чате
        /// </summary>
        public async Task<ChatReadInfoDTO?> GetChatReadInfoAsync(int userId, int chatId)
        {
            var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

            if (member == null)
                return null;

            var lastReadId = member.LastReadMessageId ?? 0;

            // Запрос непрочитанных сообщений (не свои)
            var unreadQuery = context.Messages.Where(m => m.ChatId == chatId && m.Id > lastReadId && m.IsDeleted != true && m.SenderId != userId);

            var unreadCount = await unreadQuery.CountAsync();

            // ID первого непрочитанного
            int? firstUnreadId = null;
            if (unreadCount > 0)
            {
                firstUnreadId = await unreadQuery.OrderBy(m => m.Id).Select(m => m.Id).FirstOrDefaultAsync();
            }

            return new ChatReadInfoDTO
            {
                ChatId = chatId,
                LastReadMessageId = member.LastReadMessageId,
                LastReadAt = member.LastReadAt,
                UnreadCount = unreadCount,
                FirstUnreadMessageId = firstUnreadId
            };
        }

        public async Task<int> GetUnreadCountAsync(int userId, int chatId)
        {
            var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

            if (member == null)
                return 0;

            var lastReadId = member.LastReadMessageId ?? 0;

            return await context.Messages.CountAsync(m =>
                m.ChatId == chatId &&
                m.Id > lastReadId &&
                m.IsDeleted != true &&
                m.SenderId != userId);
        }

        public async Task<AllUnreadCountsDTO> GetAllUnreadCountsAsync(int userId)
        {
            var memberData = await context.ChatMembers.Where(cm => cm.UserId == userId)
                .Select(cm => new { cm.ChatId, LastReadMessageId = cm.LastReadMessageId ?? 0 })
                .AsNoTracking()
                .ToListAsync();

            if (memberData.Count == 0)
            {
                return new AllUnreadCountsDTO { Chats = [], TotalUnread = 0 };
            }

            var chatIds = memberData.ConvertAll(m => m.ChatId);

            var unreadData = await context.Messages
                .Where(m => chatIds.Contains(m.ChatId) && m.IsDeleted != true && m.SenderId != userId)
                .GroupBy(m => m.ChatId)
                .Select(g => new { ChatId = g.Key, MessageIds = g.Select(m => m.Id).ToList() })
                .AsNoTracking()
                .ToListAsync();

            var result = new List<UnreadCountDTO>();
            var totalUnread = 0;

            foreach (var member in memberData)
            {
                var chatMessages = unreadData.FirstOrDefault(u => u.ChatId == member.ChatId);
                var unreadCount = chatMessages?.MessageIds.Count(id => id > member.LastReadMessageId) ?? 0;

                if (unreadCount > 0)
                {
                    result.Add(new UnreadCountDTO(member.ChatId, unreadCount));
                    totalUnread += unreadCount;
                }
            }

            return new AllUnreadCountsDTO
            {
                Chats = result,
                TotalUnread = totalUnread
            };
        }

        public async Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds)
        {
            var chatIdList = chatIds.ToList();

            var memberData = await context.ChatMembers
                .Where(cm => cm.UserId == userId && chatIdList.Contains(cm.ChatId))
                .Select(cm => new { cm.ChatId, LastReadMessageId = cm.LastReadMessageId ?? 0 })
                .AsNoTracking()
                .ToDictionaryAsync(x => x.ChatId, x => x.LastReadMessageId);

            var result = new Dictionary<int, int>();

            foreach (var chatId in chatIdList)
            {
                if (!memberData.TryGetValue(chatId, out var lastReadId))
                {
                    result[chatId] = 0;
                    continue;
                }

                result[chatId] = await context.Messages.CountAsync(m =>
                    m.ChatId == chatId &&
                    m.Id > lastReadId &&
                    m.IsDeleted != true &&
                    m.SenderId != userId);
            }

            return result;
        }

        public async Task MarkAllAsReadAsync(int userId, int chatId)
            => await MarkAsReadAsync(userId, new MarkAsReadDTO { ChatId = chatId });

        #region Private Methods

        private async Task<int> DetermineTargetMessageIdAsync(MarkAsReadDTO request)
        {
            if (request.MessageId.HasValue)
            {
                var messageExists = await context.Messages.AnyAsync(m =>
                    m.Id == request.MessageId.Value &&
                    m.ChatId == request.ChatId &&
                    m.IsDeleted != true);

                if (!messageExists)
                {
                    throw new KeyNotFoundException(
                        $"Сообщение {request.MessageId} не найдено в чате {request.ChatId}");
                }

                return request.MessageId.Value;
            }

            return await context.Messages
                .Where(m => m.ChatId == request.ChatId && m.IsDeleted != true)
                .OrderByDescending(m => m.Id)
                .Select(m => m.Id)
                .FirstOrDefaultAsync();
        }

        private static ReadReceiptResponseDTO CreateResponse(ChatMember member, int unreadCount) => new()
        {
            ChatId = member.ChatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadCount
        };

        #endregion
    }
}