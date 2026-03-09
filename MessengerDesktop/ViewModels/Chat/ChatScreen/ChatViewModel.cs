using Avalonia.Platform.Storage;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    // ── Зависимости ──

    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;
    private readonly INotificationService _notificationService;
    private readonly IChatNotificationApiService _notificationApiService;
    private readonly IGlobalHubConnection _globalHub;

    // ── Внутреннее состояние ──

    private readonly int _chatId;
    private readonly TaskCompletionSource _initTcs = new();
    private CancellationTokenSource? _loadingCts;
    private bool _disposed;
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    // ── Менеджеры ──

    private readonly ChatMessageManager _messageManager;
    private readonly ChatAttachmentManager _attachmentManager;
    private readonly ChatMemberLoader _memberLoader;

    public ChatsViewModel Parent { get; }

    // ── События скролла ──

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    #region Observable Properties

    [ObservableProperty] private bool _isNotificationEnabled;
    [ObservableProperty] private bool _isLoadingMuteState;
    [ObservableProperty] private UserDto? _contactUser;
    [ObservableProperty] private bool _isContactOnline;
    [ObservableProperty] private string? _contactLastSeen;
    [ObservableProperty] private ChatDto? _chat;
    [ObservableProperty] private ObservableCollection<UserDto> _members = [];
    [ObservableProperty] private bool _isInitialLoading = true;
    [ObservableProperty] private bool _isLoadingOlderMessages;
    [ObservableProperty] private bool _hasNewMessages;
    [ObservableProperty] private bool _isScrolledToBottom = true;
    [ObservableProperty] private int _pollsCount;
    [ObservableProperty] private string _newMessage = string.Empty;
    [ObservableProperty] private int _userId;
    [ObservableProperty] private UserProfileDialogViewModel? _userProfileDialog;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private int? _highlightedMessageId;
    [ObservableProperty] private int _unreadCount;

    #endregion

    #region Computed Properties

    public bool IsContactChat => Chat?.Type == ChatType.Contact;
    public bool IsGroupChat
        => Chat?.Type is ChatType.Chat or ChatType.Department;
    public string? ContactAvatar => ContactUser?.Avatar;
    public string? ContactDisplayName => ContactUser?.DisplayName;
    public string? ContactUsername => ContactUser?.Username;
    public string? ContactDepartment => ContactUser?.Department;

    public string InfoPanelTitle => IsContactChat
        ? "Информация о пользователе"
        : "Информация о группе";

    public string InfoPanelSubtitle => IsContactChat
        ? IsContactOnline ? "в сети" : ContactLastSeen ?? "не в сети"
        : $"{Members.Count} участников";

    public ObservableCollection<MessageViewModel> Messages
        => _messageManager.Messages;

    public ObservableCollection<LocalFileAttachment> LocalAttachments
        => _attachmentManager.Attachments;

    public bool IsMultiLine
        => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

    public bool HasMoreNewer => _messageManager.HasMoreNewer;
    public bool ShowScrollToBottom => !IsScrolledToBottom;

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

    public bool IsChatNotificationsEnabled
    {
        get => IsNotificationEnabled;
        set
        {
            if (IsNotificationEnabled == !value) return;
            _ = ToggleChatNotificationsCommand.ExecuteAsync(null);
        }
    }

    public List<string> PopularEmojis { get; } =
    [
        "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅",
        "😭", "😤", "❤", "👍", "👎", "🎉", "🔥", "✨",
        "💯", "🙏", "👏", "🤝", "💪", "🎁", "📱", "💻",
        "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
    ];

    #endregion

    #region Constructor

    public ChatViewModel(
        int chatId,
        ChatsViewModel parent,
        IApiClientService apiClient,
        IAuthManager authManager,
        IChatInfoPanelStateStore chatInfoPanelStateStore,
        INotificationService notificationService,
        IChatNotificationApiService notificationApiService,
        IGlobalHubConnection globalHub,
        IFileDownloadService fileDownloadService,
        IStorageProvider? storageProvider = null,
        ILocalCacheService? cacheService = null)
    {
        _apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager
            ?? throw new ArgumentNullException(nameof(authManager));
        _chatInfoPanelStateStore = chatInfoPanelStateStore
            ?? throw new ArgumentNullException(nameof(chatInfoPanelStateStore));
        _notificationService = notificationService
            ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationApiService = notificationApiService
            ?? throw new ArgumentNullException(nameof(notificationApiService));
        _globalHub = globalHub
            ?? throw new ArgumentNullException(nameof(globalHub));
        Parent = parent
            ?? throw new ArgumentNullException(nameof(parent));

        _chatId = chatId;
        UserId = _authManager.Session.UserId ?? 0;

        _globalHub.SetCurrentChat(_chatId);

        _messageManager = new ChatMessageManager(
            chatId, UserId, apiClient,
            () => Members,
            fileDownloadService, notificationService, cacheService);
        _attachmentManager = new ChatAttachmentManager(
            chatId, apiClient, storageProvider);
        _memberLoader = new ChatMemberLoader(chatId, UserId, apiClient);

        Chat = new ChatDto
        {
            Id = chatId,
            Name = "Загрузка...",
            Type = ChatType.Chat
        };

        _ = InitializeChatAsync();
    }

    #endregion

    #region Public API

    public Task WaitForInitializationAsync() => _initTcs.Task;

    public void ScrollToMessageFromSearch(MessageViewModel message)
        => ScrollToMessageRequested?.Invoke(message, true);

    public void ScrollToIndexFromSearch(int index)
        => ScrollToIndexRequested?.Invoke(index, true);

    public void ScrollToMessageSilent(MessageViewModel message)
        => ScrollToMessageRequested?.Invoke(message, false);

    public void ScrollToIndexSilent(int index)
        => ScrollToIndexRequested?.Invoke(index, false);

    #endregion

    #region Property Change Handlers

    partial void OnIsContactOnlineChanged(bool value) => OnPropertyChanged(nameof(InfoPanelSubtitle));

    partial void OnMembersChanged(ObservableCollection<UserDto>? oldValue, ObservableCollection<UserDto> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnMembersCollectionChanged;

        if (newValue != null)
            newValue.CollectionChanged += OnMembersCollectionChanged;

        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    partial void OnChatChanged(ChatDto? value)
        => OnPropertyChanged(nameof(IsInfoPanelOpen));

    partial void OnContactUserChanged(UserDto? value)
    {
        OnPropertyChanged(nameof(ContactAvatar));
        OnPropertyChanged(nameof(ContactDisplayName));
        OnPropertyChanged(nameof(ContactUsername));
        OnPropertyChanged(nameof(ContactDepartment));
    }

    partial void OnIsNotificationEnabledChanged(bool value)
        => OnPropertyChanged(nameof(IsChatNotificationsEnabled));

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowScrollToBottom));

        if (!value) return;

        HasNewMessages = false;
        UnreadCount = 0;
        _ = MarkMessagesAsReadAsync();
    }

    #endregion

    #region Scroll Command

    [RelayCommand]
    private void ScrollToBottom()
    {
        ScrollToBottomRequested?.Invoke();
        HasNewMessages = false;
        UnreadCount = 0;
    }

    #endregion
}