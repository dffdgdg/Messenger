using Avalonia.Platform.Storage;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Infrastructure;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerDesktop.ViewModels.Chats;
using MessengerDesktop.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    public ChatContext Context { get; }

    public ChatMessageManager MessageManager { get; }
    public ChatAttachmentManager Attachments { get; }
    public ChatMemberLoader MemberLoader { get; }
    public ChatEditDeleteHandler EditDelete { get; }
    public ChatReplyHandler Reply { get; }
    public ChatForwardHandler Forward { get; }
    public ChatTypingHandler Typing { get; }
    public ChatVoiceHandler Voice { get; }
    public ChatInfoPanelHandler InfoPanel { get; }
    public ChatSearchHandler Search { get; }
    public ChatNotificationHandler Notification { get; }

    private readonly ChatHubSubscriber _hubSubscriber;

    public ChatsViewModel Parent { get; }

    public ObservableCollection<MessageViewModel> Messages
        => MessageManager.Messages;

    public ObservableCollection<LocalFileAttachment> LocalAttachments
        => Attachments.Attachments;

    public string InfoPanelTitle => InfoPanel.InfoPanelTitle;
    public string InfoPanelSubtitle => InfoPanel.InfoPanelSubtitle;

    public string? ContactAvatar => InfoPanel.ContactAvatar;
    public string? ContactDisplayName => InfoPanel.ContactDisplayName;
    public string? ContactUsername => InfoPanel.ContactUsername;
    public string? ContactDepartment => InfoPanel.ContactDepartment;
    public string? ContactLastSeen => InfoPanel.ContactLastSeen;
    public bool IsContactOnline => InfoPanel.IsContactOnline;

    public ObservableCollection<UserDto> Members => Context.Members;

    public bool IsLoadingMuteState => Notification.IsLoadingMuteState;

    public bool IsChatNotificationsEnabled
    {
        get => Notification.IsNotificationEnabled;
        set
        {
            if (Notification.IsNotificationEnabled == value) return;
            _ = Notification.ToggleCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void ToggleInfoPanel()
    => IsInfoPanelOpen = !IsInfoPanelOpen;

    public ChatDto? Chat
    {
        get => Context.Chat;
        set => Context.Chat = value;
    }

    public bool IsSearchMode
    {
        get => Search.IsSearchMode;
        set
        {
            Search.IsSearchMode = value;
            OnPropertyChanged();
        }
    }

    public bool IsInfoPanelOpen
    {
        get => InfoPanel.IsInfoPanelOpen;
        set
        {
            InfoPanel.IsInfoPanelOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsGroupChat => InfoPanel.IsGroupChat;

    public bool IsContactChat => InfoPanel.IsContactChat;

    public Task ScrollToMessageAsync(int messageId)
        => Search.ScrollToMessageAsync(messageId);

    [ObservableProperty]
    private string _newMessage = string.Empty;

    [ObservableProperty]
    private bool _isInitialLoading = true;

    [ObservableProperty]
    private bool _isLoadingOlderMessages;

    [ObservableProperty]
    private bool _hasNewMessages;

    [ObservableProperty]
    private bool _isScrolledToBottom = true;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private int _pollsCount;

    [ObservableProperty]
    private int _userId;

    [ObservableProperty]
    private UserProfileDialogViewModel? _userProfileDialog;

    public bool ShowScrollToBottom => !IsScrolledToBottom;
    public bool HasMoreNewer => MessageManager.HasMoreNewer;

    public bool IsMultiLine
        => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

    public List<string> PopularEmojis { get; } =
    [
        "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅",
        "😭", "😤", "❤", "👍", "👎", "🎉", "🔥", "✨",
        "💯", "🙏", "👏", "🤝", "💪", "🎁", "📱", "💻",
        "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
    ];

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    private readonly TaskCompletionSource _initTcs = new();
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    public ChatViewModel(
        int chatId,
        ChatsViewModel parent,
        IApiClientService apiClient,
        IAuthManager authManager,
        IChatInfoPanelStateStore chatInfoPanelStateStore,
        INotificationService notificationService,
        IChatNotificationApiService notificationApiService,
        IDialogService dialogService,
        IGlobalHubConnection globalHub,
        IFileDownloadService fileDownloadService,
        IStorageProvider? storageProvider = null,
        ILocalCacheService? cacheService = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));

        var currentUserId = authManager?.Session.UserId ?? 0;
        UserId = currentUserId;

        Context = new ChatContext(
            chatId,
            currentUserId,
            apiClient ?? throw new ArgumentNullException(nameof(apiClient)),
            dialogService ?? throw new ArgumentNullException(nameof(dialogService)),
            globalHub ?? throw new ArgumentNullException(nameof(globalHub)),
            notificationService ?? throw new ArgumentNullException(nameof(notificationService)),
            notificationApiService ?? throw new ArgumentNullException(nameof(notificationApiService)),
            fileDownloadService ?? throw new ArgumentNullException(nameof(fileDownloadService)),
            cacheService);

        Context.ScrollToMessageRequested += (msg, hl)
            => ScrollToMessageRequested?.Invoke(msg, hl);
        Context.ScrollToIndexRequested += (idx, hl)
            => ScrollToIndexRequested?.Invoke(idx, hl);
        Context.ScrollToBottomRequested += ()
            => ScrollToBottomRequested?.Invoke();

        globalHub.SetCurrentChat(chatId);

        MessageManager = new ChatMessageManager(chatId, currentUserId, apiClient, () => Context.Members,
            fileDownloadService, notificationService, cacheService);

        Attachments = new ChatAttachmentManager(chatId, apiClient, storageProvider);

        MemberLoader = new ChatMemberLoader(chatId, currentUserId, apiClient);

        EditDelete = new ChatEditDeleteHandler(Context);
        Reply = new ChatReplyHandler(Context, MessageManager);
        Forward = new ChatForwardHandler(Context);
        Typing = new ChatTypingHandler(Context);
        Voice = new ChatVoiceHandler(Context, () => Reply.CancelReply());
        InfoPanel = new ChatInfoPanelHandler(Context, chatInfoPanelStateStore, MemberLoader);
        Search = new ChatSearchHandler(Context, MessageManager);
        Notification = new ChatNotificationHandler(Context);

        _hubSubscriber = new ChatHubSubscriber(Context, MessageManager, Voice, count => UnreadCount = count, OnHubReconnectedAsync);
        _hubSubscriber.Subscribe();

        Typing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatTypingHandler.TypingText))
                OnPropertyChanged(nameof(TypingText));
        };

        EditDelete.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatEditDeleteHandler.IsEditMode))
                OnPropertyChanged(nameof(IsEditMode));
        };

        Reply.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatReplyHandler.IsReplyMode))
                OnPropertyChanged(nameof(IsReplyMode));
        };

        Forward.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatForwardHandler.IsForwardMode))
                OnPropertyChanged(nameof(IsForwardMode));
        };

        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatSearchHandler.IsSearchMode))
                OnPropertyChanged(nameof(IsSearchMode));
        };

        InfoPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatInfoPanelHandler.IsInfoPanelOpen))
                OnPropertyChanged(nameof(IsInfoPanelOpen));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.IsGroupChat))
                OnPropertyChanged(nameof(IsGroupChat));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.IsContactChat))
                OnPropertyChanged(nameof(IsContactChat));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.InfoPanelTitle))
                OnPropertyChanged(nameof(InfoPanelTitle));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.InfoPanelSubtitle))
                OnPropertyChanged(nameof(InfoPanelSubtitle));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.ContactAvatar))
                OnPropertyChanged(nameof(ContactAvatar));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.ContactDisplayName))
                OnPropertyChanged(nameof(ContactDisplayName));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.ContactUsername))
                OnPropertyChanged(nameof(ContactUsername));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.ContactDepartment))
                OnPropertyChanged(nameof(ContactDepartment));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.ContactLastSeen))
                OnPropertyChanged(nameof(ContactLastSeen));
            if (e.PropertyName == nameof(ChatInfoPanelHandler.IsContactOnline))
                OnPropertyChanged(nameof(IsContactOnline));
        };

        Context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatContext.Chat))
                OnPropertyChanged(nameof(Chat));

            if (e.PropertyName == nameof(ChatContext.Members))
            {
                OnPropertyChanged(nameof(Members));
                OnPropertyChanged(nameof(InfoPanelSubtitle));
            }
        };

        Notification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatNotificationHandler.IsLoadingMuteState))
                OnPropertyChanged(nameof(IsLoadingMuteState));
            if (e.PropertyName == nameof(ChatNotificationHandler.IsNotificationEnabled))
                OnPropertyChanged(nameof(IsChatNotificationsEnabled));
        };

        Voice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatVoiceHandler.IsVoiceRecording))
                OnPropertyChanged(nameof(IsVoiceRecording));
        };

        Context.Chat = new ChatDto
        {
            Id = chatId,
            Name = "Загрузка...",
            Type = ChatType.Chat
        };

        _ = InitializeAsync();
    }

    public string TypingText => Typing.TypingText;
    public bool IsEditMode => EditDelete.IsEditMode;
    public bool IsReplyMode => Reply.IsReplyMode;
    public bool IsForwardMode => Forward.IsForwardMode;
    public bool IsVoiceRecording => Voice.IsVoiceRecording;

    private async Task InitializeAsync()
    {
        try
        {
            IsInitialLoading = true;

            var chatResult = await Context.Api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(Context.ChatId), Context.LifetimeToken);

            if (chatResult is { Success: true, Data: not null })
            {
                if (!string.IsNullOrEmpty(chatResult.Data.Avatar))
                    chatResult.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(chatResult.Data.Avatar);
                Context.Chat = chatResult.Data;
            }
            else
            {
                throw new System.Net.Http.HttpRequestException($"Ошибка загрузки чата: {chatResult.Error}");
            }

            Context.Members = await MemberLoader.LoadMembersAsync(
                Context.Chat, Context.LifetimeToken);

            if (InfoPanel.IsContactChat)
                await InfoPanel.LoadContactUserAsync();

            OnPropertyChanged(nameof(InfoPanel));

            var readInfo = await Context.Hub.GetReadInfoAsync(Context.ChatId);
            MessageManager.SetReadInfo(readInfo);

            var scrollToIndex = await MessageManager.LoadInitialMessagesAsync(Context.LifetimeToken);

            await Notification.LoadSettingsAsync(Context.LifetimeToken);

            PollsCount = MessageManager.GetPollsCount();

            var audioRecorder = App.Current.Services.GetRequiredService<IAudioRecorderService>();
            Voice.Initialize(audioRecorder);

            InfoPanel.Subscribe();

            _initTcs.TrySetResult();

            await Task.Delay(150, Context.LifetimeToken);

            if (scrollToIndex < Messages.Count - 1)
                Context.RequestScrollToIndex(scrollToIndex.Value);
            else
                Context.RequestScrollToBottom();
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] Init error: {ex.Message}");
            ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";

            CleanupOnError();

            _initTcs.TrySetException(ex);
        }
        finally
        {
            IsInitialLoading = false;
        }
    }

    partial void OnNewMessageChanged(string value)
        => Typing.NotifyTextChanged(value);

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowScrollToBottom));

        if (!value) return;

        HasNewMessages = false;
        UnreadCount = 0;
        _ = MarkMessagesAsReadAsync();
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (Context.IsDisposed) return;

        if (EditDelete.IsEditMode)
        {
            await EditDelete.SaveEditCommand.ExecuteAsync(null);
            return;
        }

        var forwarding = Forward.ForwardingMessage;
        var hasForward = forwarding != null;
        var hasText = !string.IsNullOrWhiteSpace(NewMessage);
        var hasAttachments = LocalAttachments.Count > 0;

        if (!hasText && !hasAttachments && !hasForward)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await Attachments.UploadAllAsync(ct);

            var content = NewMessage;
            if (hasForward && string.IsNullOrWhiteSpace(content))
                content = forwarding!.Content;

            var msg = new MessageDto
            {
                ChatId = Context.ChatId,
                Content = content,
                SenderId = Context.CurrentUserId,
                Files = files,
                ReplyToMessageId = Reply.ReplyingToMessage?.Id,
                ForwardedFromMessageId = forwarding?.Id
            };

            if (hasForward && files.Count == 0 && forwarding!.Files.Count > 0)
            {
                msg.Files = forwarding.Files;
            }

            var result = await Context.Api.PostAsync<MessageDto, MessageDto>
            (ApiEndpoints.Messages.Create, msg, ct);

            if (result.Success)
            {
                NewMessage = string.Empty;
                Attachments.Clear();
                Reply.CancelReply();
                Forward.CancelForward();
            }
            else
            {
                ErrorMessage = $"Ошибка отправки: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading) return;

        try
        {
            IsLoadingOlderMessages = true;
            await MessageManager.LoadOlderMessagesAsync(Context.LifetimeToken);
            PollsCount = MessageManager.GetPollsCount();
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading || !MessageManager.HasMoreNewer)
            return;

        await MessageManager.LoadNewerMessagesAsync(Context.LifetimeToken);
        PollsCount = MessageManager.GetPollsCount();
    }

    [RelayCommand]
    private void ScrollToBottom()
    {
        Context.RequestScrollToBottom();
        HasNewMessages = false;
        UnreadCount = 0;
    }

    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;
        await MarkMessagesAsReadAsync();
    }

    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment)
        => Attachments.Remove(attachment);

    [RelayCommand]
    private void InsertEmoji(string emoji)
        => NewMessage += emoji;

    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await Attachments.PickAndAddFilesAsync())
            ErrorMessage = "Не удалось выбрать файлы";
    }

    [RelayCommand]
    private async Task LeaveChat()
    {
        await SafeExecuteAsync(async ct =>
        {
            var result = await Context.Api.PostAsync(ApiEndpoints.Chats.Leave(Context.ChatId, UserId), null, ct);

            if (result.Success)
                SuccessMessage = "Вы покинули чат";
            else
                ErrorMessage = $"Ошибка при выходе из чата: {result.Error}";
        });
    }

    [RelayCommand]
    private async Task OpenCreatePoll()
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowPollDialogAsync(Context.ChatId, async () => await MessageManager.LoadInitialMessagesAsync());
            return;
        }

        await Context.Notifications.ShowErrorAsync("Не удалось открыть диалог опроса", copyToClipboard: false);
    }

    [RelayCommand]
    private async Task OpenEditChat()
    {
        if (!InfoPanel.IsGroupChat || Context.Chat == null)
            return;

        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowEditGroupDialogAsync(Context.Chat, updatedChat =>
            {
                Context.Chat = updatedChat;
                OnPropertyChanged(nameof(Chat));

                for (var i = 0; i < Parent.Chats.Count; i++)
                {
                    if (Parent.Chats[i].Id != updatedChat.Id)
                        continue;
                    Parent.Chats[i] = new ChatListItemViewModel(updatedChat);
                    break;
                }

                if (Parent.SelectedChat?.Id == updatedChat.Id)
                {
                    Parent.SelectedChat = Parent.Chats.FirstOrDefault(c => c.Id == updatedChat.Id)
                        ?? new ChatListItemViewModel(updatedChat);
                }

                _ = InfoPanel.ReloadMembersAfterEditAsync();

                OnPropertyChanged(nameof(IsGroupChat));
                OnPropertyChanged(nameof(IsContactChat));
            });
            return;
        }

        await Context.Notifications.ShowErrorAsync("Не удалось открыть редактирование чата", copyToClipboard: false);
    }

    [RelayCommand]
    public async Task OpenProfile(int userId)
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowUserProfileAsync(userId);
            return;
        }

        await Context.Notifications.ShowErrorAsync("Не удалось открыть профиль", copyToClipboard: false);
    }

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (Context.IsDisposed) return;

        if (!message.IsUnread || message.SenderId == Context.CurrentUserId)
            return;

        message.IsUnread = false;
        MessageManager.MarkAsReadLocally(message.Id);

        await Context.Hub.MarkMessageAsReadAsync(Context.ChatId, message.Id);
    }

    public async Task MarkMessagesAsReadAsync()
    {
        if (Context.IsDisposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
        await Context.Hub.MarkChatAsReadAsync(Context.ChatId);
    }

    public async Task OnMessagesVisibleAsync()
        => await MarkMessagesAsReadAsync();

    private async Task OnHubReconnectedAsync()
    {
        try
        {
            var ct = Context.LifetimeToken;
            await Task.WhenAll(MessageManager.GapFillAfterReconnectAsync(ct), RefreshInfoPanelAsync(ct));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] Reconnect error: {ex.Message}");
        }
    }

    private async Task RefreshInfoPanelAsync(CancellationToken ct)
    {
        try
        {
            var chatResult = await Context.Api.GetAsync<ChatDto>
                (ApiEndpoints.Chats.ById(Context.ChatId), ct);

            if (chatResult is { Success: true, Data: not null })
            {
                Context.Chat = chatResult.Data;
                OnPropertyChanged(nameof(Chat));
            }

            await InfoPanel.ReloadMembersAfterEditAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] RefreshInfoPanel error: {ex.Message}");
        }
    }

    public Task WaitForInitializationAsync() => _initTcs.Task;

    public void ScrollToMessageFromSearch(MessageViewModel message)
        => Context.RequestScrollToMessage(message, true);

    public void ScrollToIndexFromSearch(int index)
        => Context.RequestScrollToIndex(index, true);

    public void ScrollToMessageSilent(MessageViewModel message)
        => Context.RequestScrollToMessage(message, false);

    public void ScrollToIndexSilent(int index)
        => Context.RequestScrollToIndex(index, false);

    private void UpdatePollsCount()
        => PollsCount = MessageManager.GetPollsCount();

    private void CleanupOnError()
    {
        _hubSubscriber.Dispose();
        InfoPanel.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Context.IsDisposed) return;

        _hubSubscriber.Dispose();

        Context.Hub.SetCurrentChat(null);

        EditDelete.Dispose();
        Reply.Dispose();
        Forward.Dispose();
        Typing.Dispose();
        Voice.Dispose();
        InfoPanel.Dispose();
        Search.Dispose();
        Notification.Dispose();

        Attachments.Dispose();

        Context.Dispose();

        await Task.CompletedTask;
    }
}