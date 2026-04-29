using MessengerDesktop.Infrastructure.Helpers;
using System;

namespace MessengerDesktop.ViewModels.ChatList.Search;

public sealed class SearchMessageResultViewModel
{
    public GlobalSearchMessageDto Source { get; }

    public int Id => Source.Id;
    public int ChatId => Source.ChatId;
    public string? ChatName => Source.ChatName;
    public string? ChatAvatar => Source.ChatAvatar;
    public ChatType ChatType => Source.ChatType;
    public DateTime CreatedAt => Source.CreatedAt;
    public string? SenderName { get; }

    public string Preview { get; }
    public bool HideSenderPrefix { get; }

    public SearchMessageResultViewModel(GlobalSearchMessageDto dto, int? currentUserId = null)
    {
        Source = dto;
        SenderName = ChatPreviewFormatter.FormatSenderName(dto.SenderName, dto.SenderId, currentUserId);
        (Preview, HideSenderPrefix) = BuildPreview(dto);
    }

    private static (string Preview, bool HidePrefix) BuildPreview(GlobalSearchMessageDto msg)
    {
        if (msg.HasPoll)
        {
            var question = ChatPreviewFormatter.BuildContentPreview(msg.Content);
            return ($"📊 {question}", HidePrefix: true);
        }

        if (msg.HasVoice)
            return ("Голосовое сообщение", HidePrefix: false);

        var text = msg.HighlightedContent ?? msg.Content;

        if (msg.HasFiles && string.IsNullOrWhiteSpace(text))
            return ("Вложение", HidePrefix: false);

        if (msg.HasFiles)
            return ($"{text}", HidePrefix: false);

        return (ChatPreviewFormatter.BuildContentPreview(text), HidePrefix: false);
    }
}