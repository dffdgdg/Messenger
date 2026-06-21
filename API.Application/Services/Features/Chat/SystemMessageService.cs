using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Enum;
using Shared.Hubs;

namespace API.Application.Services.Features.Chat;

public sealed class SystemMessageService(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IMessageRepository messageRepository,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    AppDateTime appDateTime,
    ILogger<SystemMessageService> logger)
    : BaseService<SystemMessageService>(unitOfWork, logger), ISystemMessageService
{
    public async Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null, string? content = null)
    {
        try
        {
            var chat = await chatRepository.FindByIdAsync(chatId);
            if (chat is null || chat.Type == ChatType.Contact)
                return;

            var message = new SystemMessage
            {
                ChatId = chatId,
                InitiatorId = senderId,
                SystemEventType = eventType,
                TargetUserId = targetUserId,
                Content = content,
                CreatedAt = appDateTime.UtcNow,
                IsDeleted = false
            };

            messageRepository.Add(message);
            chat.LastMessageTime = appDateTime.UtcNow;
            await unitOfWork.SaveChangesAsync();

            var loaded = await messageRepository.FindSystemMessageWithIncludesAsync(message.Id)
                ?? throw new InvalidOperationException("Не удалось загрузить системное сообщение");

            await hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.ReceiveMessage, loaded.ToDto(urlBuilder: urlBuilder));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка создания системного сообщения [{EventType}] в чате {ChatId}", eventType, chatId);
        }
    }

    public async Task CreateCallEndedMessageAsync(int chatId, int initiatorId, TimeSpan duration)
    {
        var durationText = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";

        await CreateAsync(chatId, initiatorId, SystemEventType.CallEnded, content: $"Звонок завершён · {durationText}");
    }

    public async Task CreateCallStartedMessageAsync(int chatId, int initiatorId)
        => await CreateAsync(chatId, initiatorId, SystemEventType.CallStarted);
}