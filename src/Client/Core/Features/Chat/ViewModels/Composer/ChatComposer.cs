using Avalonia.Input;
using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Handlers.Collaboration;
using Core.Features.Chat.ViewModels.Handlers.Editing;
using Core.Features.Chat.ViewModels.Handlers.Media;
using Core.Features.MessageList.ViewModels.Managers;

namespace Core.Features.Chat.ViewModels.Composer;

/// <summary>
/// Управляет полем ввода: текст сообщения, @упоминания, вложения, отправка.
/// </summary>
public sealed partial class ChatComposer : ObservableObject, IDisposable
{
    private readonly ChatContext _ctx;
    private readonly ChatAttachmentManager _attachments;
    private readonly ChatReplyHandler _reply;
    private readonly ChatForwardHandler _forward;
    private readonly ChatEditDeleteHandler _editDelete;
    private readonly ChatMessageManager _messageManager;

    private int _caretIndex;

    private string _newMessage = string.Empty;
    public string NewMessage
    {
        get => _newMessage;
        set
        {
            if (_newMessage == value) return;
            _newMessage = value;
            OnPropertyChanged(nameof(NewMessage));
            HandleNewMessageChanged(value);
        }
    }

    [ObservableProperty]
    public partial bool IsMentionSuggestionsOpen { get; set; }

    [ObservableProperty]
    public partial int MentionSelectedIndex { get; set; } = -1;

    public ObservableCollection<UserDto> MentionSuggestions { get; } = [];
    public ObservableCollection<UserDto> Members => _ctx.Members;

    public bool CanSendMessageNow => !string.IsNullOrWhiteSpace(NewMessage) || _attachments.Attachments.Count > 0
        || _forward.ForwardingMessage != null;

    public bool ShowSendMessageButton => CanSendMessageNow && !_editDelete.IsEditMode;
    public bool ShowVoiceMessageButton => !CanSendMessageNow && !_editDelete.IsEditMode;
    public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

    public List<string> PopularEmojis { get; } =
    [
        "😀","😂","😍","🥰","😊","😎","🤔","😅","😭","😤",
        "❤","👍","👎","🎉","🔥","✨","💯","🙏","👏","🤝",
        "💪","🎁","📱","💻","🎮","🎵","📷","🌟","⭐","🌈","☀️","🌙"
    ];

    public event Action? ComposerStateChanged;

    public ChatComposer(ChatContext ctx, ChatAttachmentManager attachments, ChatReplyHandler reply, ChatForwardHandler forward,
        ChatEditDeleteHandler editDelete, ChatMessageManager messageManager)
    {
        _ctx = ctx;
        _attachments = attachments;
        _reply = reply;
        _forward = forward;
        _editDelete = editDelete;
        _messageManager = messageManager;

        _attachments.Attachments.CollectionChanged += (_, _) => NotifyStateChanged();
    }

    private void HandleNewMessageChanged(string value)
    {
        _caretIndex = Math.Clamp(_caretIndex, 0, value?.Length ?? 0);
        UpdateMentionSuggestions(value, _caretIndex);
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanSendMessageNow));
        OnPropertyChanged(nameof(ShowSendMessageButton));
        OnPropertyChanged(nameof(ShowVoiceMessageButton));
        ComposerStateChanged?.Invoke();
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (_ctx.IsDisposed) return;

        var forwarding = _forward.ForwardingMessage;
        var hasText = !string.IsNullOrWhiteSpace(NewMessage);
        var hasAttachments = _attachments.Attachments.Count > 0;

        if (!hasText && !hasAttachments && forwarding == null) return;

        using var cts = new CancellationTokenSource();
        var files = await _attachments.UploadAllAsync(cts.Token);

        var content = NewMessage;
        if (forwarding != null && string.IsNullOrWhiteSpace(content))
            content = forwarding.Content;

        List<MessageFileDto>? forwardFiles = null;
        if (forwarding != null && files.Count == 0 && forwarding.Files is { Count: > 0 })
            forwardFiles = forwarding.Files;

        var request = new CreateMessageRequest
        {
            ChatId = _ctx.ChatId,
            Content = content,
            ReplyToMessageId = _reply.ReplyingToMessage?.Id,
            ForwardedFromMessageId = forwarding?.Id,
            Files = files.Count > 0 ? files : forwardFiles
        };

        var result = await _ctx.Api.PostAsync<CreateMessageRequest, MessageDto>(ApiEndpoints.Messages.Create, request, cts.Token);

        if (result.Success && result.Data != null)
        {
            NewMessage = string.Empty;
            _attachments.Clear();
            _reply.CancelReply();
            _forward.CancelForward();
            NotifyStateChanged();

            Dispatcher.UIThread.Post(() =>
            {
                _messageManager.AddReceivedMessage(result.Data);
                _ctx.RequestScrollToBottom();
            });
        }

        if (!result.Success)
            throw new InvalidOperationException(result.Error);
    }

    [RelayCommand]
    public void SelectMention(UserDto? user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.Username)) return;

        var text = NewMessage ?? string.Empty;
        var caret = Math.Clamp(_caretIndex, 0, text.Length);
        if (!TryFindMentionToken(text, caret, out var start, out _)) return;

        var prefix = text[..start];
        var suffix = caret < text.Length ? text[caret..] : string.Empty;
        NewMessage = $"{prefix}@{user.Username} {suffix}";
        _caretIndex = (prefix + "@" + user.Username + " ").Length;
        IsMentionSuggestionsOpen = false;
        MentionSelectedIndex = -1;
    }

    public bool HandleNavigationKey(Key key)
    {
        if (!IsMentionSuggestionsOpen || MentionSuggestions.Count == 0) return false;

        switch (key)
        {
            case Key.Down:
                MentionSelectedIndex = MentionSelectedIndex < MentionSuggestions.Count - 1 ? MentionSelectedIndex + 1 : 0;
                return true;
            case Key.Up:
                MentionSelectedIndex = MentionSelectedIndex > 0 ? MentionSelectedIndex - 1 : MentionSuggestions.Count - 1;
                return true;
            case Key.Enter:
                return TryConfirmSelection();
            case Key.Escape:
                IsMentionSuggestionsOpen = false;
                MentionSelectedIndex = -1;
                return true;
            default:
                return false;
        }
    }

    public void OnCaretChanged(int caretIndex)
    {
        _caretIndex = caretIndex;
        UpdateMentionSuggestions(NewMessage, caretIndex);
    }

    private bool TryConfirmSelection()
    {
        if (MentionSelectedIndex < 0 || MentionSelectedIndex >= MentionSuggestions.Count)
            return false;
        SelectMention(MentionSuggestions[MentionSelectedIndex]);
        return true;
    }

    private void UpdateMentionSuggestions(string? text, int caretIndex)
    {
        var source = _ctx.Members.Where(m => m.Id != _ctx.CurrentUserId && !string.IsNullOrWhiteSpace(m.Username)).ToList();

        if (!TryFindMentionToken(text ?? string.Empty, caretIndex, out _, out var token))
        {
            MentionSuggestions.Clear();
            IsMentionSuggestionsOpen = false;
            MentionSelectedIndex = -1;
            return;
        }

        var filtered = source.Where(m => m.Username!.Contains(token, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Username!.StartsWith(token, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.Username).Take(7).ToList();

        MentionSuggestions.Clear();
        foreach (var member in filtered)
            MentionSuggestions.Add(member);

        IsMentionSuggestionsOpen = MentionSuggestions.Count > 0;
        MentionSelectedIndex = IsMentionSuggestionsOpen ? 0 : -1;
    }

    private static bool TryFindMentionToken(string text, int caretIndex, out int tokenStart, out string token)
    {
        tokenStart = -1;
        token = string.Empty;

        if (string.IsNullOrEmpty(text) || caretIndex < 0 || caretIndex > text.Length)
            return false;

        var i = caretIndex - 1;
        while (i >= 0 && !char.IsWhiteSpace(text[i])) i--;

        tokenStart = i + 1;
        if (tokenStart >= text.Length || text[tokenStart] != '@') return false;
        if (caretIndex <= tokenStart) return false;

        token = text.Substring(tokenStart + 1, caretIndex - tokenStart - 1);
        return token.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    [RelayCommand]
    public void InsertEmoji(string emoji) => NewMessage += emoji;

    public void Dispose() => MentionSuggestions.Clear();
}