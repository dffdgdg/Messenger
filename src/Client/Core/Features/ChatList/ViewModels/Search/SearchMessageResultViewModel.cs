using Core.Shared.Helpers;
using Shared.Contracts.Search;

namespace Core.Features.ChatList.ViewModels.Search;

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

    public string PreView { get; }
    public bool HideSenderPrefix { get; }

    public SearchMessageResultViewModel(GlobalSearchMessageDto dto, int? currentUserId = null)
    {
        Source = dto;
        SenderName = ChatPreViewFormatter.FormatSenderName(dto.SenderName, dto.SenderId, currentUserId);
        (PreView, HideSenderPrefix) = BuildPreView(dto);
    }

    private static (string PreView, bool HidePrefix) BuildPreView(GlobalSearchMessageDto msg)
    {
        if (msg.HasPoll)
        {
            var question = ChatPreViewFormatter.BuildContentPreView(msg.Content);
            return ($"📊 {question}", HidePrefix: true);
        }

        if (msg.HasVoice)
            return ("Голосовое сообщение", HidePrefix: false);

        var text = msg.HighlightedContent ?? msg.Content;

        if (msg.HasFiles && string.IsNullOrWhiteSpace(text))
            return ("Вложение", HidePrefix: false);

        if (msg.HasFiles)
            return ($"{text}", HidePrefix: false);

        return (ChatPreViewFormatter.BuildContentPreView(text), HidePrefix: false);
    }
}