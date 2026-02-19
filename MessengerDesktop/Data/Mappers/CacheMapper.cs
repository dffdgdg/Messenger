using MessengerDesktop.Data.Entities;
using MessengerShared.DTO;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Poll;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MessengerDesktop.Data.Mappers;

/// <summary>
/// Маппинг между сетевыми DTO и локальными entity-классами.
/// Статический, без состояния, без зависимостей.
/// </summary>
public static class CacheMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    #region MessageDTO ↔ CachedMessage

    public static CachedMessage ToEntity(this MessageDTO dto)
    {
        return new CachedMessage
        {
            Id = dto.Id,
            ChatId = dto.ChatId,
            SenderId = dto.SenderId,
            Content = dto.Content,
            CreatedAtTicks = dto.CreatedAt.ToUniversalTime().Ticks,
            EditedAtTicks = dto.EditedAt?.ToUniversalTime().Ticks,
            IsDeleted = dto.IsDeleted,
            IsVoiceMessage = dto.IsVoiceMessage,
            TranscriptionStatus = dto.TranscriptionStatus,
            ReplyToMessageId = dto.ReplyToMessageId,
            ForwardedFromMessageId = dto.ForwardedFromMessageId,
            IsOwn = dto.IsOwn,

            // Sender
            SenderName = dto.SenderName,
            SenderAvatarUrl = dto.SenderAvatarUrl,

            // Reply preview
            ReplySenderName = dto.ReplyToMessage?.SenderName,
            ReplyContentPreview = dto.ReplyToMessage?.Content,
            ReplyIsDeleted = dto.ReplyToMessage?.IsDeleted ?? false,
            ReplySenderId = dto.ReplyToMessage?.SenderId,
            ReplyChatId = dto.ReplyToMessage?.ChatId,

            // Forward info
            ForwardSenderName = dto.ForwardedFrom?.OriginalSenderName,
            ForwardOriginalSenderId = dto.ForwardedFrom?.OriginalSenderId,
            ForwardOriginalChatId = dto.ForwardedFrom?.OriginalChatId,
            ForwardOriginalDateTicks = dto.ForwardedFrom?.OriginalCreatedAt.ToUniversalTime().Ticks,

            // JSON blobs
            PollJson = dto.Poll != null ? JsonSerializer.Serialize(dto.Poll, JsonOpts) : null,
            FilesJson = dto.Files is { Count: > 0 } ? JsonSerializer.Serialize(dto.Files, JsonOpts) : null,

            CachedAtTicks = DateTime.UtcNow.Ticks
        };
    }

    public static MessageDTO ToDto(this CachedMessage entity)
    {
        var dto = new MessageDTO
        {
            Id = entity.Id,
            ChatId = entity.ChatId,
            SenderId = entity.SenderId,
            Content = entity.Content,
            CreatedAt = entity.CreatedAt,
            EditedAt = entity.EditedAt,
            IsEdited = entity.EditedAtTicks.HasValue,
            IsDeleted = entity.IsDeleted,
            IsVoiceMessage = entity.IsVoiceMessage,
            TranscriptionStatus = entity.TranscriptionStatus,
            ReplyToMessageId = entity.ReplyToMessageId,
            ForwardedFromMessageId = entity.ForwardedFromMessageId,
            IsOwn = entity.IsOwn,
            SenderName = entity.SenderName,
            SenderAvatarUrl = entity.SenderAvatarUrl,
        };

        // Reply preview
        if (entity.ReplyToMessageId.HasValue && entity.ReplySenderName != null)
        {
            dto.ReplyToMessage = new MessageReplyPreviewDTO
            {
                Id = entity.ReplyToMessageId.Value,
                SenderId = entity.ReplySenderId ?? 0,
                ChatId = entity.ReplyChatId ?? 0,
                SenderName = entity.ReplySenderName,
                Content = entity.ReplyContentPreview,
                IsDeleted = entity.ReplyIsDeleted
            };
        }

        // Forward info
        if (entity.ForwardedFromMessageId.HasValue)
        {
            dto.ForwardedFrom = new MessageForwardInfoDTO
            {
                OriginalMessageId = entity.ForwardedFromMessageId.Value,
                OriginalSenderId = entity.ForwardOriginalSenderId ?? 0,
                OriginalChatId = entity.ForwardOriginalChatId ?? 0,
                OriginalSenderName = entity.ForwardSenderName,
                OriginalCreatedAt = entity.ForwardOriginalDateTicks.HasValue
                    ? new DateTime(entity.ForwardOriginalDateTicks.Value, DateTimeKind.Utc)
                    : default
            };
        }

        // Poll (JSON → DTO)
        if (!string.IsNullOrEmpty(entity.PollJson))
        {
            try
            {
                dto.Poll = JsonSerializer.Deserialize<PollDTO>(entity.PollJson, JsonOpts);
            }
            catch
            {
                // Corrupted JSON — пропускаем, не ломаем приложение
            }
        }

        // Files (JSON → List<DTO>)
        if (!string.IsNullOrEmpty(entity.FilesJson))
        {
            try
            {
                dto.Files = JsonSerializer.Deserialize<List<MessageFileDTO>>(entity.FilesJson, JsonOpts) ?? [];
            }
            catch
            {
                dto.Files = [];
            }
        }

        return dto;
    }

    #endregion

    #region ChatDTO ↔ CachedChat

    public static CachedChat ToEntity(this ChatDTO dto)
    {
        return new CachedChat
        {
            Id = dto.Id,
            Name = dto.Name,
            Type = (int)dto.Type,
            Avatar = dto.Avatar,
            CreatedById = dto.CreatedById,
            LastMessageDateTicks = dto.LastMessageDate?.ToUniversalTime().Ticks,
            LastMessagePreview = dto.LastMessagePreview,
            LastMessageSenderName = dto.LastMessageSenderName,
            CachedAtTicks = DateTime.UtcNow.Ticks
        };
    }

    public static ChatDTO ToDto(this CachedChat entity)
    {
        return new ChatDTO
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = (ChatType)entity.Type,
            Avatar = entity.Avatar,
            CreatedById = entity.CreatedById,
            LastMessageDate = entity.LastMessageDate,
            LastMessagePreview = entity.LastMessagePreview,
            LastMessageSenderName = entity.LastMessageSenderName
        };
    }

    #endregion

    #region UserDTO ↔ CachedUser

    public static CachedUser ToEntity(this MessengerShared.DTO.User.UserDTO dto)
    {
        return new CachedUser
        {
            Id = dto.Id,
            Username = dto.Username,
            DisplayName = dto.DisplayName,
            Avatar = dto.Avatar,
            CachedAtTicks = DateTime.UtcNow.Ticks
        };
    }

    public static MessengerShared.DTO.User.UserDTO ToDto(this CachedUser entity)
    {
        return new MessengerShared.DTO.User.UserDTO
        {
            Id = entity.Id,
            Username = entity.Username,
            DisplayName = entity.DisplayName,
            Avatar = entity.Avatar
        };
    }

    #endregion
}