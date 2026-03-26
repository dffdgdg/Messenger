using MessengerDesktop.ViewModels.Dialog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatForwardHandler : ChatFeatureHandler
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForwardMode))]
    [NotifyPropertyChangedFor(nameof(ForwardPreviewText))]
    [NotifyPropertyChangedFor(nameof(ForwardingSenderName))]
    public partial MessageViewModel? ForwardingMessage { get; set; }

    public bool IsForwardMode => ForwardingMessage != null;

    public string? ForwardingSenderName => ForwardingMessage?.SenderName;

    public string? ForwardPreviewText => ForwardingMessage switch
    {
        null => null,
        { IsDeleted: true } => "[Сообщение удалено]",
        { IsVoiceMessage: true } => "Голосовое сообщение",
        { HasPoll: true } => "Опрос",
        { HasFiles: true, Content: null or "" } => $"📎 {ForwardingMessage.Files.Count} файл(ов)",
        { Content: { } c } => c.Length > 100 ? c[..100] + "…" : c, _ => "[Сообщение]"
    };

    public ChatForwardHandler(ChatContext context) : base(context)
        => Ctx.CompositionModeReset += OnCompositionReset;

    [RelayCommand]
    private async Task StartForward(MessageViewModel? message)
    {
        if (message?.IsDeleted != false) return;

        Ctx.ResetCompositionModes();
        ForwardingMessage = message;

        var chatsResult = await Ctx.Api.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(Ctx.CurrentUserId));
        if (!chatsResult.Success || chatsResult.Data == null)
        {
            await Ctx.Notifications.ShowErrorAsync(chatsResult.Error ?? "Не удалось загрузить список чатов для пересылки");
            CancelForward();
            return;
        }

        var availableChats = chatsResult.Data.Where(c => c.Id != Ctx.ChatId).OrderByDescending(c => c.LastMessageDate).ToList();

        if (availableChats.Count == 0)
        {
            await Ctx.Notifications.ShowWarningAsync("Нет доступных чатов для пересылки");
            CancelForward();
            return;
        }

        var picker = new ChatPickerDialogViewModel("Выберите чат для пересылки", availableChats);
        await Ctx.Dialogs.ShowAsync(picker);

        var targetChat = await picker.SingleSelectResult;
        if (targetChat == null)
        {
            CancelForward();
            return;
        }

        var payload = new MessageDto
        {
            ChatId = targetChat.Id,
            SenderId = Ctx.CurrentUserId,
            Content = string.IsNullOrWhiteSpace(message.Content) ? null : message.Content,
            ForwardedFromMessageId = message.Id,
            Files = message.Files
        };

        var sendResult = await Ctx.Api.PostAsync<MessageDto, MessageDto>(ApiEndpoints.Messages.Create, payload);
        if (sendResult.Success)
            await Ctx.Notifications.ShowSuccessAsync($"Сообщение переслано в чат «{targetChat.Name}»");
        else
            await Ctx.Notifications.ShowErrorAsync(sendResult.Error ?? "Не удалось переслать сообщение");

        CancelForward();
    }

    [RelayCommand]
    public void CancelForward()
        => ForwardingMessage = null;

    private void OnCompositionReset()
    {
        if (IsForwardMode) CancelForward();
    }

    public override void Dispose()
    {
        Ctx.CompositionModeReset -= OnCompositionReset;
        base.Dispose();
    }
}
