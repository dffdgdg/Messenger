using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Helpers;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerShared.DTO;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;
    private readonly INotificationService _notificationService;
    private readonly int _chatId;
    private readonly IChatNotificationApiService _notificationApiService;
    private readonly IGlobalHubConnection _globalHub;
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    private readonly ChatMessageManager _messageManager;
    private readonly ChatAttachmentManager _attachmentManager;
    private ChatHubConnection? _hubConnection;
    private readonly ChatMemberLoader _memberLoader;

    private CancellationTokenSource? _loadingCts;
    private CancellationTokenSource? _searchCts;
    private bool _disposed;
    private readonly TaskCompletionSource _initTcs = new();

    public ChatsViewModel Parent { get; }

    public event Action<MessageViewModel>? ScrollToMessageRequested;

    #region Observable Properties

    [ObservableProperty]
    private bool isNotificationEnabled;

    [ObservableProperty]
    private bool isLoadingMuteState;

    [ObservableProperty]
    private UserDTO? contactUser;

    [ObservableProperty]
    private bool isContactOnline;

    [ObservableProperty]
    private string? contactLastSeen;

    public bool IsContactChat => Chat?.Type == ChatType.Contact;

    public bool IsGroupChat => Chat?.Type == ChatType.Chat || Chat?.Type == ChatType.Department;

    public string InfoPanelTitle => IsContactChat ? "Информация о пользователе" : "Информация о группе";

    public string InfoPanelSubtitle
    {
        get
        {
            if (IsContactChat)
            {
                if (IsContactOnline) return "в сети";
                if (!string.IsNullOrEmpty(ContactLastSeen)) return ContactLastSeen;
                return "не в сети";
            }
            return $"{Members.Count} участников";
        }
    }

    [ObservableProperty]
    private ChatDTO? chat;

    [ObservableProperty]
    private ObservableCollection<UserDTO> members = [];

    [ObservableProperty]
    private bool isInitialLoading = true;

    [ObservableProperty]
    private bool isLoadingOlderMessages;

    [ObservableProperty]
    private bool hasNewMessages;

    [ObservableProperty]
    private bool isScrolledToBottom = true;

    /// <summary>
    /// Событие для скролла к определённому индексу сообщения
    /// </summary>
    public event Action<int>? ScrollToIndexRequested;
    [ObservableProperty]
    private int pollsCount;

    [ObservableProperty]
    private string newMessage = string.Empty;

    [ObservableProperty]
    private int userId;

    [ObservableProperty]
    private UserProfileDialogViewModel? userProfileDialog;

    [ObservableProperty]
    private bool isSearchMode;

    [ObservableProperty]
    private int? highlightedMessageId;

    #endregion

    #region Public Properties

    public ObservableCollection<MessageViewModel> Messages => _messageManager.Messages;
    public ObservableCollection<LocalFileAttachment> LocalAttachments => _attachmentManager.Attachments;

    public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

    public bool IsInfoPanelOpen
    {
        get => _chatInfoPanelStateStore.IsOpen;
        set
        {
            if (_chatInfoPanelStateStore.IsOpen == value) return;
            _chatInfoPanelStateStore.IsOpen = value;
            OnPropertyChanged();
        }
    }

    public List<string> PopularEmojis { get; } =
    [
        "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅", "😭", "😤",
        "❤", "👍", "👎", "🎉", "🔥", "✨", "💯", "🙏", "👏", "🤝",
        "💪", "🎁", "📱", "💻", "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
    ];

    public bool IsChatNotificationsEnabled
    {
        get => IsNotificationEnabled;
        set
        {
            if (IsNotificationEnabled == !value) return;
            _ = ToggleChatNotificationsCommand.ExecuteAsync(null);
        }
    }

    #endregion

    public ChatViewModel(int chatId,ChatsViewModel parent,IApiClientService apiClient,IAuthManager authManager,IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService,IChatNotificationApiService notificationApiService,IGlobalHubConnection globalHub,IFileDownloadService fileDownloadService,
    IStorageProvider? storageProvider = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatInfoPanelStateStore = chatInfoPanelStateStore ?? throw new ArgumentNullException(nameof(chatInfoPanelStateStore));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationApiService = notificationApiService ?? throw new ArgumentNullException(nameof(notificationApiService));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));

        _chatId = chatId;
        UserId = _authManager.Session.UserId ?? 0;

        _globalHub.SetCurrentChat(_chatId);

        _messageManager = new ChatMessageManager(
            chatId, UserId, apiClient, () => Members,
            fileDownloadService, notificationService);

        _attachmentManager = new ChatAttachmentManager(
            chatId, apiClient, storageProvider);

        _memberLoader = new ChatMemberLoader(chatId, UserId, apiClient);

        Chat = new ChatDTO
        {
            Id = chatId,
            Name = "Загрузка...",
            Type = ChatType.Chat
        };

        _ = InitializeChatAsync();
    }

    public Task WaitForInitializationAsync() => _initTcs.Task;

    #region Initialization

    private async Task InitializeChatAsync()
    {
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        try
        {
            IsInitialLoading = true;

            await LoadChatAsync(ct);
            await LoadMembersAsync(ct);
            await InitHubAsync(ct);

            var readInfo = await _hubConnection!.GetReadInfoAsync();
            _messageManager.SetReadInfo(readInfo);

            var scrollToIndex = await _messageManager.LoadInitialMessagesAsync(ct);

            await LoadNotificationSettingsAsync(ct);

            UpdatePollsCount();
            _initTcs.TrySetResult();

            if (scrollToIndex.HasValue)
            {
                await Task.Delay(150, ct);
                ScrollToIndexRequested?.Invoke(scrollToIndex.Value);
            }
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";
            _initTcs.TrySetException(ex);
        }
        finally
        {
            IsInitialLoading = false;
        }
    }

    private async Task LoadNotificationSettingsAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _notificationApiService.GetChatSettingsAsync(_chatId, ct);
            if (settings != null)
            {
                IsNotificationEnabled = settings.NotificationsEnabled;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка загрузки настроек уведомлений: {ex.Message}");
        }
    }

    /// <summary>
    /// Вызывается из View когда сообщение становится видимым
    /// </summary>
    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (!message.IsUnread || message.SenderId == UserId)
            return;

        // Отмечаем локально
        message.IsUnread = false;
        _messageManager.MarkAsReadLocally(message.Id);

        // Отправляем на сервер
        if (_hubConnection != null)
        {
            await _hubConnection.MarkMessageAsReadAsync(message.Id);
        }
    }
    /// <summary>
    /// Загрузить более новые сообщения
    /// </summary>
    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (_messageManager.IsLoading || !_messageManager.HasMoreNewer) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;
        await _messageManager.LoadNewerMessagesAsync(ct);
        UpdatePollsCount();
    }
    [RelayCommand]
    private async Task ToggleChatNotifications()
    {
        if (IsLoadingMuteState) return;

        try
        {
            IsLoadingMuteState = true;
            var newNotificationsState = !IsNotificationEnabled;

            var success = await _notificationApiService.SetChatMuteAsync(_chatId, newNotificationsState);

            if (success)
            {
                IsNotificationEnabled = newNotificationsState;

                var message = newNotificationsState
                    ? "Уведомления включены для этого чата"
                    : "Уведомления отключены для этого чата";

                await _notificationService.ShowInfoAsync(message);
            }
            else
            {
                await _notificationService.ShowErrorAsync("Не удалось изменить настройки");
            }
        }
        catch (Exception ex)
        {
            await _notificationService.ShowErrorAsync($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoadingMuteState = false;
        }
    }

    partial void OnIsNotificationEnabledChanged(bool value) => OnPropertyChanged(nameof(IsChatNotificationsEnabled));

    private async Task LoadChatAsync(CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<ChatDTO>($"api/chats/{_chatId}", ct);

        if (result.Success && result.Data is not null)
        {
            if (!string.IsNullOrEmpty(result.Data.Avatar))
                result.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(result.Data.Avatar);

            Chat = result.Data;
        }
        else
        {
            throw new HttpRequestException($"Ошибка загрузки чата: {result.Error}");
        }
    }

    private async Task LoadMembersAsync(CancellationToken ct)
    {
        Members = await _memberLoader.LoadMembersAsync(Chat, ct);

        if (IsContactChat)
        {
            await LoadContactUserAsync();
        }

        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    private async Task LoadContactUserAsync()
    {
        try
        {
            var contact = Members.FirstOrDefault(m => m.Id != UserId);

            if (contact != null)
            {
                ContactUser = contact;
                IsContactOnline = contact.IsOnline;

                if (!contact.IsOnline && contact.LastOnline.HasValue)
                {
                    ContactLastSeen = FormatLastSeen(contact.LastOnline.Value);
                }

                if (Chat != null)
                {
                    Chat.Name = contact.DisplayName ?? contact.Username ?? Chat.Name;
                    if (!string.IsNullOrEmpty(contact.Avatar))
                    {
                        Chat.Avatar = contact.Avatar;
                    }
                }
            }

            OnPropertyChanged(nameof(IsContactChat));
            OnPropertyChanged(nameof(IsGroupChat));
            OnPropertyChanged(nameof(InfoPanelTitle));
            OnPropertyChanged(nameof(InfoPanelSubtitle));
            OnPropertyChanged(nameof(ContactUser));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadContactUser error: {ex.Message}");
        }
    }

    private static string FormatLastSeen(DateTime lastOnline)
    {
        var now = DateTime.Now;
        var diff = now - lastOnline;

        if (diff.TotalMinutes < 1)
            return "был(а) только что";
        if (diff.TotalMinutes < 60)
            return $"был(а) {(int)diff.TotalMinutes} мин. назад";
        if (diff.TotalHours < 24)
            return $"был(а) {(int)diff.TotalHours} ч. назад";
        if (diff.TotalDays < 2)
            return "был(а) вчера";
        if (diff.TotalDays < 7)
            return $"был(а) {(int)diff.TotalDays} дн. назад";

        return $"был(а) {lastOnline:dd.MM.yyyy}";
    }

    private async Task InitHubAsync(CancellationToken ct)
    {
        _hubConnection = new ChatHubConnection(_chatId, _authManager);
        _hubConnection.MessageReceived += OnMessageReceived;
        _hubConnection.MessageRead += OnMessageRead;
        await _hubConnection.ConnectAsync(ct);
    }

    private void OnMessageReceived(MessageDTO messageDto)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            _messageManager.AddReceivedMessage(messageDto);
            UpdatePollsCount();

            if (!IsScrolledToBottom)
            {
                HasNewMessages = true;
            }
            else
            {
                await MarkMessagesAsReadAsync();
            }
        });
    }

    private void OnMessageRead(int chatId, int userId, int? lastReadMessageId, DateTime? readAt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (lastReadMessageId.HasValue)
            {
                foreach (var msg in Messages.Where(m => m.Id <= lastReadMessageId.Value && m.SenderId == UserId))
                {
                    msg.IsRead = true;
                }
            }
        });
    }

    #endregion

    #region Mark As Read

    /// <summary>
    /// Отмечает сообщения как прочитанные с debounce
    /// </summary>
    public async Task MarkMessagesAsReadAsync(int? messageId = null)
    {
        if ((DateTime.UtcNow - _lastMarkAsReadTime).TotalSeconds < 1)
            return;

        _lastMarkAsReadTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        if (_hubConnection is not null)
        {
            await _hubConnection.MarkAsReadAsync(messageId);
        }
    }

    /// <summary>
    /// Вызывается когда пользователь проскроллил и видит сообщения
    /// </summary>
    public async Task OnMessagesVisibleAsync() => await MarkMessagesAsReadAsync();
    /// <summary>
    /// Есть ли более новые сообщения для загрузки
    /// </summary>
    public bool HasMoreNewer => _messageManager.HasMoreNewer;
    partial void OnIsScrolledToBottomChanged(bool value)
    {
        if (value)
        {
            HasNewMessages = false;
            _ = MarkMessagesAsReadAsync();
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (_messageManager.IsLoading) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            IsLoadingOlderMessages = true;
            await _messageManager.LoadOlderMessagesAsync(ct);
            UpdatePollsCount();
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(NewMessage) && LocalAttachments.Count == 0)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await _attachmentManager.UploadAllAsync(ct);

            var msg = new MessageDTO
            {
                ChatId = _chatId,
                Content = NewMessage,
                SenderId = UserId,
                Files = files
            };

            var result = await _apiClient.PostAsync<MessageDTO, MessageDTO>("api/messages", msg, ct);

            if (result.Success)
            {
                NewMessage = string.Empty;
                _attachmentManager.Clear();
            }
            else
            {
                ErrorMessage = $"Ошибка отправки: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment) => _attachmentManager.Remove(attachment);

    [RelayCommand]
    private void InsertEmoji(string emoji) => NewMessage += emoji;

    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await _attachmentManager.PickAndAddFilesAsync())
        {
            ErrorMessage = "Не удалось выбрать файлы";
        }
    }

    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelOpen = !IsInfoPanelOpen;

    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;

        await MarkMessagesAsReadAsync();
    }

    [RelayCommand]
    private async Task LeaveChat()
    {
        await SafeExecuteAsync(async ct =>
        {
            var result = await _apiClient.PostAsync($"api/chats/{_chatId}/leave?userId={UserId}", null, ct);

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
            await menu.ShowPollDialogAsync(_chatId, async () =>
                await _messageManager.LoadInitialMessagesAsync());
            return;
        }

        await _notificationService.ShowErrorAsync("Не удалось открыть диалог опроса", copyToClipboard: false);
    }

    [RelayCommand]
    public async Task OpenProfile(int userId)
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowUserProfileAsync(userId);
            return;
        }

        await _notificationService.ShowErrorAsync("Не удалось открыть профиль", copyToClipboard: false);
    }

    #endregion

    #region Search Commands & Methods

    public async Task ScrollToMessageAsync(int messageId)
    {
        try
        {
            var existingMessage = Messages.FirstOrDefault(m => m.Id == messageId);

            if (existingMessage != null)
            {
                var index = Messages.IndexOf(existingMessage);
                ScrollToIndexRequested?.Invoke(index);
                HighlightMessage(existingMessage);
                return;
            }

            var targetIndex = await _messageManager.LoadMessagesAroundAsync(messageId);

            if (targetIndex.HasValue)
            {
                await Task.Delay(100);
                ScrollToIndexRequested?.Invoke(targetIndex.Value);

                var targetMessage = Messages.FirstOrDefault(m => m.Id == messageId);
                if (targetMessage != null)
                {
                    HighlightMessage(targetMessage);
                }
            }
            else
            {
                Debug.WriteLine($"[ChatViewModel] Failed to load messages around {messageId}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ScrollToMessage error: {ex.Message}");
        }
    }

    private void HighlightMessage(MessageViewModel message)
    {
        foreach (var m in Messages)
        {
            m.IsHighlighted = false;
        }

        message.IsHighlighted = true;
        HighlightedMessageId = message.Id;

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            Dispatcher.UIThread.Post(() =>
            {
                message.IsHighlighted = false;
                HighlightedMessageId = null;
            });
        });
    }

    [RelayCommand]
    private async Task GoToSearchResult(MessageViewModel? searchResult)
    {
        if (searchResult == null) return;

        IsSearchMode = false;

        await ScrollToMessageAsync(searchResult.Id);
    }

    #endregion

    #region Helpers

    private void UpdatePollsCount() => PollsCount = _messageManager.GetPollsCount();

    partial void OnChatChanged(ChatDTO? value) => OnPropertyChanged(nameof(IsInfoPanelOpen));

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_globalHub is GlobalHubConnection hub)
        {
            hub.SetCurrentChat(null);
        }

        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        if (_hubConnection is not null)
        {
            _hubConnection.MessageReceived -= OnMessageReceived;
            _hubConnection.MessageRead -= OnMessageRead;
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        Messages.Clear();
        Members.Clear();
        LocalAttachments.Clear();
        _attachmentManager.Dispose();

        Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}