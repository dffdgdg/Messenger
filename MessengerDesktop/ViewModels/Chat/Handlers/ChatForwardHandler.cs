namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatForwardHandler : ChatFeatureHandler
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForwardMode))]
    [NotifyPropertyChangedFor(nameof(ForwardPreviewText))]
    [NotifyPropertyChangedFor(nameof(ForwardingSenderName))]
    private MessageViewModel? _forwardingMessage;

    public bool IsForwardMode => ForwardingMessage != null;

    public string? ForwardingSenderName => ForwardingMessage?.SenderName;

    public string? ForwardPreviewText => ForwardingMessage switch
    {
        null => null,
        { IsDeleted: true } => "[Сообщение удалено]",
        { IsVoiceMessage: true } => "Голосовое сообщение",
        { HasPoll: true } => "Опрос",
        { HasFiles: true, Content: null or "" }
            => $"📎 {ForwardingMessage.Files.Count} файл(ов)",
        { Content: { } c } => c.Length > 100 ? c[..100] + "…" : c,
        _ => "[Сообщение]"
    };

    public ChatForwardHandler(ChatContext context) : base(context)
        => Ctx.CompositionModeReset += OnCompositionReset;

    [RelayCommand]
    private void StartForward(MessageViewModel? message)
    {
        if (message?.IsDeleted != false) return;

        Ctx.ResetCompositionModes();
        ForwardingMessage = message;
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
