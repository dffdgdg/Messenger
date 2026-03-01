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

/// <summary>
/// ViewModel экрана чата. Partial-класс, логика распределена по файлам:
/// <list type="bullet">
///   <item><description>ChatViewModel.cs — состояние и свойства</description></item>
///   <item><description>ChatViewModel.Commands.cs — UI-команды (кнопки)</description></item>
///   <item><description>ChatViewModel.Editing.cs — редактирование/удаление сообщений</description></item>
///   <item><description>ChatViewModel.Init.cs — инициализация, загрузка, dispose</description></item>
///   <item><description>ChatViewModel.Messaging.cs — отправка, приём, скролл</description></item>
///   <item><description>ChatViewModel.Reply.cs — ответы на сообщения</description></item>
///   <item><description>ChatViewModel.Search.cs — поиск и навигация по результатам</description></item>
///   <item><description>ChatViewModel.Voice.cs — голосовые сообщения и транскрипция</description></item>
/// </list>
/// </summary>
public partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    // ── Зависимости ──────────────────────────────────────────────────

    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;
    private readonly INotificationService _notificationService;
    private readonly IChatNotificationApiService _notificationApiService;
    private readonly IGlobalHubConnection _globalHub;

    // ── Внутреннее состояние ─────────────────────────────────────────

    private readonly int _chatId;
    private readonly TaskCompletionSource _initTcs = new();
    private CancellationTokenSource? _loadingCts;
    private bool _disposed;
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    // ── Менеджеры ────────────────────────────────────────────────────

    private readonly ChatMessageManager _messageManager;
    private readonly ChatAttachmentManager _attachmentManager;
    private readonly ChatMemberLoader _memberLoader;
    private ChatHubConnection? _hubConnection;

    /// <summary>Родительская ViewModel списка чатов.</summary>
    public ChatsViewModel Parent { get; }

    // ── События скролла ──────────────────────────────────────────────

    /// <summary>Запрос скролла к конкретному сообщению. bool — нужна ли подсветка.</summary>
    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;

    /// <summary>Запрос скролла к индексу в коллекции. bool — нужна ли подсветка.</summary>
    public event Action<int, bool>? ScrollToIndexRequested;

    /// <summary>Запрос скролла в самый низ.</summary>
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

    /// <summary>Является ли чат личной перепиской (1 на 1).</summary>
    public bool IsContactChat => Chat?.Type == ChatType.Contact;

    /// <summary>Является ли чат групповым или департаментным.</summary>
    public bool IsGroupChat => Chat?.Type is ChatType.Chat or ChatType.Department;

    /// <summary>Безопасные прокси-свойства для биндингов контактного чата.</summary>
    public string? ContactAvatar => ContactUser?.Avatar;
    public string? ContactDisplayName => ContactUser?.DisplayName;
    public string? ContactUsername => ContactUser?.Username;
    public string? ContactDepartment => ContactUser?.Department;

    /// <summary>Заголовок инфо-панели в зависимости от типа чата.</summary>
    public string InfoPanelTitle => IsContactChat
        ? "Информация о пользователе"
        : "Информация о группе";

    /// <summary>Подзаголовок: онлайн-статус для контакта, кол-во участников для группы.</summary>
    public string InfoPanelSubtitle => IsContactChat
        ? IsContactOnline ? "в сети" : ContactLastSeen ?? "не в сети"
        : $"{Members.Count} участников";

    /// <summary>Коллекция сообщений (проксируется из менеджера).</summary>
    public ObservableCollection<MessageViewModel> Messages => _messageManager.Messages;

    /// <summary>Локальные вложения, ожидающие отправки.</summary>
    public ObservableCollection<LocalFileAttachment> LocalAttachments => _attachmentManager.Attachments;

    /// <summary>Содержит ли текст переноса строки (для адаптации высоты поля ввода).</summary>
    public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

    /// <summary>Есть ли более новые сообщения, которые ещё не подгружены.</summary>
    public bool HasMoreNewer => _messageManager.HasMoreNewer;

    /// <summary>Нужно ли показывать кнопку «вниз».</summary>
    public bool ShowScrollToBottom => !IsScrolledToBottom;

    /// <summary>Состояние боковой инфо-панели (хранится в store для сохранения между чатами).</summary>
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

    /// <summary>
    /// Проперти-обёртка для toggle-кнопки уведомлений.
    /// При установке запускает команду переключения.
    /// </summary>
    public bool IsChatNotificationsEnabled
    {
        get => IsNotificationEnabled;
        set
        {
            if (IsNotificationEnabled == !value) return;
            _ = ToggleChatNotificationsCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Набор популярных эмодзи для быстрой вставки.</summary>
    public List<string> PopularEmojis { get; } =
    [
        "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅", "😭", "😤",
        "❤", "👍", "👎", "🎉", "🔥", "✨", "💯", "🙏", "👏", "🤝",
        "💪", "🎁", "📱", "💻", "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
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
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatInfoPanelStateStore = chatInfoPanelStateStore ?? throw new ArgumentNullException(nameof(chatInfoPanelStateStore));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationApiService = notificationApiService ?? throw new ArgumentNullException(nameof(notificationApiService));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));

        _chatId = chatId;
        UserId = _authManager.Session.UserId ?? 0;

        // Уведомляем глобальный хаб о текущем активном чате
        _globalHub.SetCurrentChat(_chatId);

        // Инициализация менеджеров
        _messageManager = new ChatMessageManager(
            chatId, UserId, apiClient, () => Members, fileDownloadService, notificationService, cacheService);
        _attachmentManager = new ChatAttachmentManager(chatId, apiClient, storageProvider);
        _memberLoader = new ChatMemberLoader(chatId, UserId, apiClient);

        // Заглушка до загрузки реальных данных — предотвращает binding-ошибки
        Chat = new ChatDto { Id = chatId, Name = "Загрузка...", Type = ChatType.Chat };

        // Запуск асинхронной инициализации (fire-and-forget)
        _ = InitializeChatAsync();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Ожидание завершения инициализации чата.
    /// Используется в тестах и при необходимости дождаться готовности.
    /// </summary>
    public Task WaitForInitializationAsync() => _initTcs.Task;

    /// <summary>Скролл к сообщению из поиска (с подсветкой).</summary>
    public void ScrollToMessageFromSearch(MessageViewModel message)
        => ScrollToMessageRequested?.Invoke(message, true);

    /// <summary>Скролл к индексу из поиска (с подсветкой).</summary>
    public void ScrollToIndexFromSearch(int index)
        => ScrollToIndexRequested?.Invoke(index, true);

    /// <summary>Скролл к сообщению без подсветки (внутреннее использование).</summary>
    public void ScrollToMessageSilent(MessageViewModel message)
        => ScrollToMessageRequested?.Invoke(message, false);

    /// <summary>Скролл к индексу без подсветки (внутреннее использование).</summary>
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

    partial void OnChatChanged(ChatDto? value) => OnPropertyChanged(nameof(IsInfoPanelOpen));

    partial void OnContactUserChanged(UserDto? value)
    {
        OnPropertyChanged(nameof(ContactAvatar));
        OnPropertyChanged(nameof(ContactDisplayName));
        OnPropertyChanged(nameof(ContactUsername));
        OnPropertyChanged(nameof(ContactDepartment));
    }

    partial void OnIsNotificationEnabledChanged(bool value) =>
        OnPropertyChanged(nameof(IsChatNotificationsEnabled));

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowScrollToBottom));

        if (!value) return;

        // При достижении конца списка сбрасываем счётчик непрочитанных
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