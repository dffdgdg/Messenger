using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;
    private readonly INotificationService _notificationService;
    private readonly IChatNotificationApiService _notificationApiService;
    private readonly IGlobalHubConnection _globalHub;
    private readonly int _chatId;

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested; // bool = withHighlight
    public event Action<int, bool>? ScrollToIndexRequested; // bool = withHighlight
    public event Action? ScrollToBottomRequested;

    private readonly ChatMessageManager _messageManager;
    private readonly ChatAttachmentManager _attachmentManager;
    private readonly ChatMemberLoader _memberLoader;
    private ChatHubConnection? _hubConnection;

    private CancellationTokenSource? _loadingCts;
    private bool _disposed;
    private readonly TaskCompletionSource _initTcs = new();
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    public ChatsViewModel Parent { get; }

    #region Observable Properties

    [ObservableProperty] private bool _isNotificationEnabled;
    [ObservableProperty] private bool _isLoadingMuteState;
    [ObservableProperty] private UserDTO? _contactUser;
    [ObservableProperty] private bool _isContactOnline;
    [ObservableProperty] private string? _contactLastSeen;
    [ObservableProperty] private ChatDTO? _chat;
    [ObservableProperty] private ObservableCollection<UserDTO> _members = [];
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
    public bool IsGroupChat => Chat?.Type == ChatType.Chat || Chat?.Type == ChatType.Department;
    public string InfoPanelTitle => IsContactChat ? "Информация о пользователе" : "Информация о группе";

    public string InfoPanelSubtitle => IsContactChat
        ? IsContactOnline ? "в сети" : ContactLastSeen ?? "не в сети"
        : $"{Members.Count} участников";

    public ObservableCollection<MessageViewModel> Messages => _messageManager.Messages;
    public ObservableCollection<LocalFileAttachment> LocalAttachments => _attachmentManager.Attachments;
    public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');
    public bool HasMoreNewer => _messageManager.HasMoreNewer;

    public bool ShowScrollToBottom => !IsScrolledToBottom;

    /// <summary>
    /// Скролл к сообщению из поиска (с подсветкой)
    /// </summary>
    public void ScrollToMessageFromSearch(MessageViewModel message)
    {
        ScrollToMessageRequested?.Invoke(message, true); // withHighlight = true
    }

    /// <summary>
    /// Скролл к индексу из поиска (с подсветкой)
    /// </summary>
    public void ScrollToIndexFromSearch(int index)
    {
        ScrollToIndexRequested?.Invoke(index, true); // withHighlight = true
    }

    /// <summary>
    /// Скролл к сообщению БЕЗ подсветки (внутреннее использование)
    /// </summary>
    public void ScrollToMessageSilent(MessageViewModel message)
        => ScrollToMessageRequested?.Invoke(message, false); // withHighlight = false

    /// <summary>
    /// Скролл к индексу БЕЗ подсветки (внутреннее использование)
    /// </summary>
    public void ScrollToIndexSilent(int index)
        => ScrollToIndexRequested?.Invoke(index, false); // withHighlight = false

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
        "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅", "😭", "😤",
        "❤", "👍", "👎", "🎉", "🔥", "✨", "💯", "🙏", "👏", "🤝",
        "💪", "🎁", "📱", "💻", "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
    ];

    #endregion

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

        _messageManager = new ChatMessageManager(chatId, UserId, apiClient, () => Members, fileDownloadService, notificationService);
        _attachmentManager = new ChatAttachmentManager(chatId, apiClient, storageProvider);
        _memberLoader = new ChatMemberLoader(chatId, UserId, apiClient);

        Chat = new ChatDTO { Id = chatId, Name = "Загрузка...", Type = ChatType.Chat };

        _ = InitializeChatAsync();
    }

    public Task WaitForInitializationAsync() => _initTcs.Task;

    partial void OnChatChanged(ChatDTO? value) => OnPropertyChanged(nameof(IsInfoPanelOpen));
    partial void OnIsNotificationEnabledChanged(bool value) => OnPropertyChanged(nameof(IsChatNotificationsEnabled));

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowScrollToBottom));

        if (value)
        {
            HasNewMessages = false;
            UnreadCount = 0;
            _ = MarkMessagesAsReadAsync();
        }
    }

    [RelayCommand]
    private void ScrollToBottom()
    {
        ScrollToBottomRequested?.Invoke();
        HasNewMessages = false;
        UnreadCount = 0;
    }
}