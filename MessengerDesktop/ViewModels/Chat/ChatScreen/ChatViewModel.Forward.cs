using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>Сообщение, которое пользователь пересылает</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForwardMode))]
    private MessageViewModel? _forwardingMessage;

    /// <summary>Активен ли режим пересылки.</summary>
    public bool IsForwardMode => ForwardingMessage != null;

    /// <summary>Имя отправителя пересылаемого сообщения для безопасного биндинга</summary>
    public string? ForwardingSenderName => ForwardingMessage?.SenderName;

    /// <summary>Превью текста пересылаемого сообщения для панели ввода</summary>
    public string? ForwardPreviewText => ForwardingMessage switch
    {
        null => null,
        { IsDeleted: true } => "[Сообщение удалено]",
        { IsVoiceMessage: true } => "Голосовое сообщение",
        { HasPoll: true } => "Опрос",
        { HasFiles: true, Content: null or "" } => $"📎 {ForwardingMessage.Files.Count} файл(ов)",
        { Content: { } c } => c.Length > 100 ? c[..100] + "…" : c,
        _ => "[Сообщение]"
    };

    /// <summary>
    /// Начать пересылку сообщения.
    /// Автоматически выходит из режимов редактирования и ответа.
    /// </summary>
    [RelayCommand]
    private void StartForward(MessageViewModel? message)
    {
        if (message?.IsDeleted != false) return;

        CancelEditMessage();
        CancelReply();
        ForwardingMessage = message;
        OnPropertyChanged(nameof(ForwardPreviewText));
        OnPropertyChanged(nameof(ForwardingSenderName));
    }

    /// <summary>Отменить пересылку.</summary>
    [RelayCommand]
    private void CancelForward()
    {
        ForwardingMessage = null;
        OnPropertyChanged(nameof(ForwardPreviewText));
        OnPropertyChanged(nameof(ForwardingSenderName));
    }
}