using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Helpers;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.ViewModels.Chat;
using MessengerDesktop.ViewModels.Chats;
using MessengerDesktop.ViewModels.Dialog;
using MessengerDesktop.ViewModels.Factories;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class ChatsViewModel : BaseViewModel, IRefreshable
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatViewModelFactory _chatViewModelFactory;
    private readonly IGlobalHubConnection _globalHub;
    private readonly ILocalCacheService _cacheService;

    private ChatViewModel? _subscribedChatVm;
    private bool _isFirstLoad = true;
    private bool _disposed;

    IAsyncRelayCommand IRefreshable.RefreshCommand => LoadChatsCommand;

    public MainMenuViewModel Parent { get; }

    public UserProfileDialogViewModel? UserProfileDialog
    {
        get => CurrentChatViewModel?.UserProfileDialog;
        set { CurrentChatViewModel?.UserProfileDialog = value; }
    }

    [ObservableProperty] public partial bool IsGroupMode { get; set; }
    [ObservableProperty] public partial bool IsInitialLoading { get; set; } = true;
    [ObservableProperty] public partial ObservableCollection<ChatListItemViewModel> Chats { get; set; } = [];
    [ObservableProperty] public partial ChatListItemViewModel? SelectedChat { get; set; }
    [ObservableProperty] public partial ChatViewModel? CurrentChatViewModel { get; set; }
    [ObservableProperty] public partial GlobalSearchManager? SearchManager { get; set; }
    [ObservableProperty] public partial int TotalUnreadCount { get; set; }

    public bool IsSearchMode => SearchManager?.IsSearchMode is true;
    public bool IsChatLocalSearchMode => SearchManager?.IsChatLocalMode is true;
    public bool CombinedIsInfoPanelVisible => CurrentChatViewModel?.IsInfoPanelOpen == true;

    public ChatsViewModel(MainMenuViewModel parent, bool isGroupMode, IApiClientService apiClient, IAuthManager authManager,
        IChatViewModelFactory chatViewModelFactory, IGlobalHubConnection globalHub, ILocalCacheService cacheService)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        IsGroupMode = isGroupMode;
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatViewModelFactory = chatViewModelFactory ?? throw new ArgumentNullException(nameof(chatViewModelFactory));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

        _globalHub.TotalUnreadChanged += OnTotalUnreadChanged;
        _globalHub.UnreadCountChanged += OnUnreadCountChanged;
        _globalHub.MessageReceivedGlobally += OnMessageReceivedGlobally;

        InitializeSearchManager();
        _ = LoadChats().ContinueWith(
            t => Debug.WriteLine($"[ChatsVM] Initial load failed: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void InitializeSearchManager()
    {
        if (!_authManager.Session.UserId.HasValue) return;

        SearchManager = new GlobalSearchManager(_authManager.Session.UserId.Value, IsGroupMode, _apiClient);
        SearchManager.PropertyChanged += OnSearchManagerPropertyChanged;
    }

    private void OnSearchManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GlobalSearchManager.IsSearchMode):
                OnPropertyChanged(nameof(IsSearchMode));
                break;
            case nameof(GlobalSearchManager.IsChatLocalMode):
                OnPropertyChanged(nameof(IsChatLocalSearchMode));
                break;
            case nameof(GlobalSearchManager.IsChatsScope):
            case nameof(GlobalSearchManager.IsContactsScope):
            case nameof(GlobalSearchManager.CanSearchInCurrentChat):
                break;
        }
    }

    private void SyncSearchScopeWithChatViewMode()
    {
        if (SearchManager == null) return;

        if (CurrentChatViewModel?.IsSearchMode == true
            && SelectedChat != null
            && CurrentChatViewModel.Chat?.Id == SelectedChat.Id)
        {
            SearchManager.UseScope(SearchScopeMode.CurrentChatMessages);
        }
    }

    private void SetSearchChatContext(ChatListItemViewModel? chat)
    {
        if (SearchManager == null) return;
        SearchManager.ChatLocalSearchChatId = chat?.Id;
        SearchManager.ChatLocalSearchChatType = chat?.Type;
        SearchManager.ChatLocalSearchChatName = chat?.Name;
        SearchManager.ChatLocalSearchChatAvatar = chat?.Avatar;
    }

    [RelayCommand]
    private void CloseSearch()
    {
        SearchManager?.ExitSearch();
        if (CurrentChatViewModel?.IsSearchMode == true)
            CurrentChatViewModel.IsSearchMode = false;
    }

    [RelayCommand]
    private async Task LoadMoreSearchResults()
    {
        if (SearchManager != null)
            await SearchManager.LoadMoreMessagesAsync();
    }

    [RelayCommand]
    private void SetSearchScope(SearchScopeMode scope)
    {
        SearchManager?.UseScope(scope);
    }

    private void OnTotalUnreadChanged(int total) => TotalUnreadCount = total;

    private void OnUnreadCountChanged(int chatId, int unreadCount) => FindChat(chatId)?.Apply(c => c.UnreadCount = unreadCount);

    private void OnMessageReceivedGlobally(MessageDto message)
    {
        var chat = FindChat(message.ChatId);
        if (chat == null) return;

        var currentUserId = _authManager.Session.UserId;
        chat.LastMessageSenderName = ChatPreviewFormatter.FormatSenderName(message.SenderName, message.SenderId, currentUserId);
        chat.LastMessagePreview = ChatPreviewFormatter.BuildPreview(message);
        chat.LastMessageDate = message.CreatedAt;

        MoveChatToTop(chat);
    }

    [RelayCommand]
    private void OpenChat(ChatListItemViewModel? chat)
    {
        if (chat == null) return;
        try { SelectedChat = chat; }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия чата: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task OpenSearchedChat(ChatListItemViewModel? chat)
    {
        if (chat == null) return;

        if (!IsChatMatchingCurrentTab(chat.Type))
        {
            await Parent.SwitchToTabAndOpenChatAsync(chat.ToDto());
            SearchManager?.ExitSearch();
            return;
        }

        SelectedChat = Chats.FirstOrDefault(c => c.Id == chat.Id) ?? InsertAndReturn(chat);
        SearchManager?.ExitSearch();
    }

    [RelayCommand]
    private async Task OpenSearchResult(GlobalSearchMessageDto? searchResult)
    {
        if (searchResult == null) return;

        try
        {
            if (!IsChatMatchingCurrentTab(searchResult.ChatType))
            {
                await Parent.SwitchToTabAndOpenMessageAsync(searchResult);
                SearchManager?.ExitSearch();
                return;
            }

            await OpenChatByIdAsync(searchResult.ChatId, searchResult.Id);
            SearchManager?.ExitSearch();
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия: {ex.Message}"; }
    }

    public async Task OpenChatByIdAsync(int chatId, int? scrollToMessageId = null)
    {
        if (Chats.Count == 0)
            await LoadChats();

        var chat = FindChat(chatId) ?? await FetchAndInsertChatAsync(chatId);
        if (chat == null) return;

        SelectedChat = chat;

        if (scrollToMessageId.HasValue && CurrentChatViewModel != null)
        {
            await CurrentChatViewModel.WaitForInitializationAsync();
            await CurrentChatViewModel.ScrollToMessageAsync(scrollToMessageId.Value);
        }
    }

    public async Task OpenOrCreateDialogWithUserAsync(UserDto user)
    {
        await SafeExecuteAsync(async () =>
        {
            await LoadChats();

            var existingChat = await FindDialogWithUser(user.Id);
            if (existingChat != null)
            {
                var item = FindChat(existingChat.Id) ?? InsertAndReturn(new ChatListItemViewModel(existingChat));
                OpenChatCommand.Execute(item);
                return;
            }

            var userId = _authManager.Session.UserId ?? 0;
            var result = await _apiClient.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chats.Create,
                new ChatDto { Name = user.Id.ToString(), Type = ChatType.Contact, CreatedById = userId });

            if (!result.Success || result.Data == null)
            {
                ErrorMessage = $"Ошибка создания диалога: {result.Error}";
                return;
            }

            result.Data.Name = user.DisplayName ?? user.Username;
            result.Data.Avatar = user.Avatar;

            var created = new ChatListItemViewModel(result.Data);
            Chats.Add(created);
            OpenChatCommand.Execute(created);
        });
    }

    [RelayCommand]
    private async Task CreateGroup()
    {
        try
        {
            await Parent.ShowCreateGroupDialogAsync(createdChat =>
            {
                var item = new ChatListItemViewModel(createdChat);
                if (Chats.All(c => c.Id != createdChat.Id))
                    Chats.Insert(0, item);

                SelectedChat = FindChat(createdChat.Id) ?? item;
            });
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка создания группы: {ex.Message}"; }
    }

    partial void OnSelectedChatChanged(ChatListItemViewModel? value)
    {
        SyncSearchScopeWithChatViewMode();
        SetSearchChatContext(value);

        if (value == null) return;

        if (value.UnreadCount > 0)
        {
            value.UnreadCount = 0;
            _ = _globalHub.MarkChatAsReadAsync(value.Id);
        }

        if (CurrentChatViewModel?.Chat?.Id != value.Id)
            CurrentChatViewModel = _chatViewModelFactory.Create(value.Id, this);
    }

    partial void OnCurrentChatViewModelChanged(ChatViewModel? oldValue, ChatViewModel? newValue)
    {
        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged -= OnChatVmPropertyChanged;

        _subscribedChatVm = newValue;

        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged += OnChatVmPropertyChanged;

        SyncSearchScopeWithChatViewMode();
        OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));

        if (oldValue != null && !ReferenceEquals(oldValue, newValue))
        {
            Dispatcher.UIThread.Post(() =>
            {
                Debug.WriteLine($"[ChatsVM] Disposing ChatViewModel for chat {oldValue.Chat?.Id}");
                try { oldValue.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[ChatsVM] Dispose error: {ex.Message}"); }
            }, DispatcherPriority.Background);
        }
    }

    private void OnChatVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.IsInfoPanelOpen):
                OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
                break;

            case nameof(ChatViewModel.IsSearchMode):
                SyncSearchScopeWithChatViewMode();
                if (SearchManager == null) return;

                if (CurrentChatViewModel?.IsSearchMode == true)
                    SearchManager.EnterSearchMode();
                else
                    SearchManager.ExitSearch();
                break;
        }
    }

    [RelayCommand]
    public async Task LoadChats()
    {
        try
        {
            await SafeExecuteAsync(async () =>
            {
                if (!_authManager.Session.IsAuthenticated || !_authManager.Session.UserId.HasValue)
                {
                    ErrorMessage = "Ошибка авторизации";
                    return;
                }

                if (_isFirstLoad)
                    await ShowCachedChatsAsync();

                await LoadFreshChatsAsync(_authManager.Session.UserId.Value);
            });
        }
        finally
        {
            if (_isFirstLoad)
            {
                _isFirstLoad = false;
                IsInitialLoading = false;
            }
        }
    }

    private async Task ShowCachedChatsAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var cached = await _cacheService.GetChatsAsync(IsGroupMode);

            if (cached.Count == 0)
            {
                Debug.WriteLine($"[ChatsVM] No cached {ChatTypeLabel}, waiting for server");
                return;
            }

            foreach (var c in cached)
                c.UnreadCount = _globalHub.GetUnreadCount(c.Id);

            Chats = new ObservableCollection<ChatListItemViewModel>(cached.Select(c => new ChatListItemViewModel(c)));
            TotalUnreadCount = _globalHub.GetTotalUnread();
            IsInitialLoading = false;

            Debug.WriteLine($"[ChatsVM] Showed {cached.Count} cached {ChatTypeLabel} in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatsVM] Cache read failed (non-critical): {ex.Message}");
        }
    }

    private async Task LoadFreshChatsAsync(int userId)
    {
        var endpoint = IsGroupMode ? ApiEndpoints.Chats.UserGroups(userId) : ApiEndpoints.Chats.UserDialogs(userId);

        var result = await _apiClient.GetAsync<List<ChatDto>>(endpoint);

        if (!result.Success || result.Data == null)
        {
            if (Chats.Count == 0)
                ErrorMessage = $"Ошибка загрузки чатов: {result.Error}";
            else
                Debug.WriteLine($"[ChatsVM] Server unavailable, showing cached data. Error: {result.Error}");
            return;
        }

        var ordered = result.Data.OrderByDescending(c => c.LastMessageDate).ToList();
        foreach (var c in ordered)
            c.UnreadCount = _globalHub.GetUnreadCount(c.Id);

        var selectedId = SelectedChat?.Id;
        Chats = new ObservableCollection<ChatListItemViewModel>(ordered.Select(c => new ChatListItemViewModel(c)));
        TotalUnreadCount = _globalHub.GetTotalUnread();

        if (selectedId.HasValue)
        {
            var restored = FindChat(selectedId.Value);
            if (restored != null && SelectedChat?.Id != restored.Id)
                SelectedChat = restored;
        }

        await SaveCacheSilentAsync(ordered);
    }

    public void UpdateChatInList(ChatDto updatedChat)
    {
        for (var i = 0; i < Chats.Count; i++)
        {
            if (Chats[i].Id != updatedChat.Id) continue;
            Chats[i] = new ChatListItemViewModel(updatedChat);
            break;
        }

        if (SelectedChat?.Id == updatedChat.Id)
            SelectedChat = FindChat(updatedChat.Id) ?? new ChatListItemViewModel(updatedChat);
    }

    private ChatListItemViewModel? FindChat(int chatId) => Chats.FirstOrDefault(c => c.Id == chatId);

    private ChatListItemViewModel InsertAndReturn(ChatListItemViewModel chat)
    {
        Chats.Insert(0, chat);
        return chat;
    }

    private async Task<ChatListItemViewModel?> FetchAndInsertChatAsync(int chatId)
    {
        var result = await _apiClient.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(chatId));
        if (!result.Success || result.Data == null) return null;

        result.Data.UnreadCount = _globalHub.GetUnreadCount(chatId);
        return InsertAndReturn(new ChatListItemViewModel(result.Data));
    }

    private void MoveChatToTop(ChatListItemViewModel chat)
    {
        var idx = Chats.IndexOf(chat);
        if (idx <= 0) return;

        Chats.Move(idx, 0);
        if (SelectedChat?.Id == chat.Id)
            SelectedChat = chat;
    }

    private bool IsChatMatchingCurrentTab(ChatType type) => IsGroupMode ? type is ChatType.Chat or ChatType.Department : type == ChatType.Contact;

    private async Task<ChatDto?> FindDialogWithUser(int contactUserId)
    {
        var currentUserId = _authManager.Session.UserId ?? 0;
        var result = await _apiClient.GetAsync<ChatDto?>(ApiEndpoints.Chats.UserContact(currentUserId, contactUserId));
        return result.Success ? result.Data : null;
    }

    private async Task SaveCacheSilentAsync(List<ChatDto> chats)
    {
        try
        {
            await _cacheService.UpsertChatsAsync(chats);
            Debug.WriteLine($"[ChatsVM] Cached {chats.Count} {ChatTypeLabel}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatsVM] Cache write failed (non-critical): {ex.Message}");
        }
    }

    private string ChatTypeLabel => IsGroupMode ? "groups" : "dialogs";

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _globalHub.TotalUnreadChanged -= OnTotalUnreadChanged;
            _globalHub.UnreadCountChanged -= OnUnreadCountChanged;
            _globalHub.MessageReceivedGlobally -= OnMessageReceivedGlobally;
            SearchManager?.PropertyChanged -= OnSearchManagerPropertyChanged;
            _subscribedChatVm?.PropertyChanged -= OnChatVmPropertyChanged;

            try { CurrentChatViewModel?.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[ChatsVM] ChatVM dispose error: {ex.Message}"); }
            CurrentChatViewModel = null;
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}

file static class ObjectExtensions
{
    public static void Apply<T>(this T obj, Action<T> action) => action(obj);
}