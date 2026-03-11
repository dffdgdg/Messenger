using MessengerDesktop.ViewModels.Chat.Managers;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatReplyHandler : ChatFeatureHandler
{
    private readonly ChatMessageManager _messageManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplyMode))]
    private MessageViewModel? _replyingToMessage;

    public bool IsReplyMode => ReplyingToMessage != null;

    public ChatReplyHandler(ChatContext context, ChatMessageManager messageManager) : base(context)
    {
        _messageManager = messageManager;
        Ctx.CompositionModeReset += OnCompositionReset;
    }

    [RelayCommand]
    private void StartReply(MessageViewModel? message)
    {
        if (message?.IsDeleted != false) return;

        Ctx.ResetCompositionModes();
        ReplyingToMessage = message;
    }

    [RelayCommand]
    public void CancelReply()
        => ReplyingToMessage = null;

    [RelayCommand]
    private async Task ScrollToReplyOriginal(MessageViewModel? message)
    {
        if (message?.ReplyToMessageId == null) return;

        var targetId = message.ReplyToMessageId.Value;
        var messages = _messageManager.Messages;

        var existing = messages.FirstOrDefault(m => m.Id == targetId);
        if (existing != null)
        {
            HighlightAndScroll(existing);
            return;
        }

        var targetIndex = await _messageManager.LoadMessagesAroundAsync(targetId);
        if (targetIndex < messages.Count)
        {
            HighlightAndScroll(messages[targetIndex.Value]);
        }
    }

    private void HighlightAndScroll(MessageViewModel target)
    {
        target.IsHighlighted = true;
        Ctx.RequestScrollToMessage(target, true);

        _ = Task.Delay(AppConstants.HighlightDurationMs).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() => target.IsHighlighted = false));
    }

    private void OnCompositionReset()
    {
        if (IsReplyMode) CancelReply();
    }

    public override void Dispose()
    {
        Ctx.CompositionModeReset -= OnCompositionReset;
        base.Dispose();
    }
}
