using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Contracts.User;
using Shared.Enum;

namespace API.Application.Features.Chat.Queries;

public class GetUserChatsQueryHandler(
    IAccessControlService accessControl,
    IChatRepository chatRepository,
    IReadReceiptService readReceiptService,
    IOnlineUserService onlineService,
    IUrlBuilder urlBuilder)
    : IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>>
{
    public virtual async Task<Result<List<ChatDto>>> HandleAsync(GetUserChatsQuery query, CancellationToken ct = default)
    {
        var chatIds = await accessControl.GetUserChatIdsAsync(query.UserId);
        if (chatIds.Count == 0)
            return Result<List<ChatDto>>.Success([]);

        var chatsWithLastMessage = await LoadChatsWithLastMessageAsync(chatIds, ct);
        var unreadCounts = await readReceiptService
            .GetUnreadCountsForChatsAsync(query.UserId, chatIds);

        var dialogChatIds = chatsWithLastMessage.Where(c => c.Chat.Type == ChatType.Contact).Select(c => c.Chat.Id).ToList();

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, query.UserId, ct);
        var userRoles = await chatRepository.GetMemberRolesAsync(chatIds, query.UserId, ct);

        var result = chatsWithLastMessage
            .ConvertAll(item => BuildChatDto(item, unreadCounts, dialogPartners, userRoles));

        return Result<List<ChatDto>>.Success(
        [
            .. result.OrderByDescending(c => c.UnreadCount > 0).ThenByDescending(c => c.LastMessageDate)
        ]);
    }

    private async Task<List<ChatWithLastMessage>> LoadChatsWithLastMessageAsync(List<int> chatIds,CancellationToken ct)
    {
        var chats = await chatRepository.GetByIdsAsync(chatIds, ct);
        var lastMessages = await chatRepository.GetLastMessagesAsync(chatIds, ct);
        var lastMsgMap = lastMessages.ToDictionary(m => m.ChatId);

        return chats.ConvertAll(chat =>
        {
            var lastMsg = lastMsgMap.TryGetValue(chat.Id, out var msg)
                ? MapToLastMessageInfo(msg)
                : null;

            return new ChatWithLastMessage(chat, lastMsg);
        });
    }

    private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(
        List<int> chatIds,
        int currentUserId,
        CancellationToken ct)
    {
        if (chatIds.Count == 0) return [];

        var partners = await chatRepository.GetDialogPartnersAsync(chatIds, currentUserId, ct);
        var partnerUserIds = partners.ConvertAll(p => p.UserId);
        var onlineIds = onlineService.FilterOnline(partnerUserIds);

        return partners.ToDictionary(
            p => p.ChatId,
            p => new DialogPartnerInfo(
                UserId: p.UserId,
                DisplayName: FormatDisplayName(p.Surname, p.Name, p.Midname),
                AvatarUrl: urlBuilder.BuildUrl(p.Avatar),
                StatusType: p.StatusType,
                StatusExpiresAt: p.StatusExpiresAt,
                IsOnline: onlineIds.Contains(p.UserId)
            ));
    }

    private ChatDto BuildChatDto(
        ChatWithLastMessage item,
        Dictionary<int, int> unreadCounts,
        Dictionary<int, DialogPartnerInfo> dialogPartners,
        Dictionary<int, ChatRole> userRoles)
    {
        var msg = item.LastMessage;
        var (preview, senderName, isSystem) =
            msg is not null ? BuildLastMessagePreview(msg) : (null, null, false);

        var isContact = item.Chat.Type == ChatType.Contact;

        var dto = new ChatDto
        {
            Id = item.Chat.Id,
            Type = item.Chat.Type,
            CreatedById = item.Chat.CreatedById ?? 0,
            LastMessageDate = msg?.CreatedAt ?? item.Chat.LastMessageTime,
            LastMessagePreview = preview,
            LastMessageSenderName = isContact ? null : senderName,
            LastMessageSenderId = msg?.SenderId,
            LastMessageIsSystem = isSystem,
            LastMessageIsPoll = msg?.HasPoll ?? false,
            LastMessageIsVoice = msg?.IsVoiceMessage ?? false,
            UnreadCount = unreadCounts.GetValueOrDefault(item.Chat.Id, 0),
            ShowHistoryForNewMembers = item.Chat.ShowHistoryForNewMembers,
            CurrentUserRole = userRoles.GetValueOrDefault(item.Chat.Id)
        };

        if (isContact && dialogPartners.TryGetValue(item.Chat.Id, out var partner))
        {
            dto.Name = partner.DisplayName;
            dto.Avatar = partner.AvatarUrl;
            dto.ContactUserId = partner.UserId;
            dto.ContactIsOnline = partner.IsOnline;
            dto.ContactStatusType = partner.StatusType;
            dto.ContactStatusExpiresAt = partner.StatusExpiresAt;
        }
        else
        {
            dto.Name = item.Chat.Name;
            dto.Avatar = urlBuilder.BuildUrl(item.Chat.Avatar);
        }

        return dto;
    }

    private static (string? Preview, string? SenderName, bool IsSystem) BuildLastMessagePreview(LastMessageInfo msg)
    {
        if (msg.IsSystemMessage)
        {
            var formatted = SystemMessageFormatter.Format(
                msg.SystemEventType, msg.SenderName, msg.TargetUserName);
            return (formatted, null, true);
        }

        var preview = BuildContentPreview(msg);
        var firstName = ExtractFirstName(msg.SenderName);
        return (preview, firstName, false);
    }

    private static string? BuildContentPreview(LastMessageInfo msg)
    {
        if (msg.HasPoll)
            return $"📊 {Truncate(msg.Content, 50) ?? "Опрос"}";

        if (msg.IsVoiceMessage)
            return "Голосовое сообщение";

        if (msg.HasFiles && string.IsNullOrWhiteSpace(msg.Content))
            return "Вложение";

        return Truncate(msg.Content, 50);
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    private static string FormatDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static LastMessageInfo MapToLastMessageInfo(LastMessageProjection msg) =>
        new(msg.Id, msg.Content, msg.CreatedAt, msg.IsSystemMessage,
            msg.SenderId, msg.IsVoiceMessage, msg.SystemEventType,
            msg.TargetUserId, msg.SenderName, msg.TargetUserName,
            msg.HasPoll, msg.HasFiles);

    // Внутренние типы — локальные, не торчат наружу
    private sealed record LastMessageInfo(
        int Id, string? Content, DateTime CreatedAt, bool IsSystemMessage,
        int? SenderId, bool IsVoiceMessage, SystemEventType? SystemEventType,
        int? TargetUserId, string? SenderName, string? TargetUserName,
        bool HasPoll, bool HasFiles);

    private sealed record ChatWithLastMessage(
        Domain.Entities.Chat Chat,
        LastMessageInfo? LastMessage);

    private sealed record DialogPartnerInfo(
        int UserId, string DisplayName, string? AvatarUrl,
        UserStatusType StatusType, DateTime? StatusExpiresAt, bool IsOnline);
}

