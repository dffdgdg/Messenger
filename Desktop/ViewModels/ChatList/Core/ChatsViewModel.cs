using Desktop.Data.Repositories.Abstractions;
using Desktop.Infrastructure.Diagnostics;
using Desktop.Infrastructure.Helpers;
using Desktop.ViewModels.Chat;
using Desktop.ViewModels.ChatList.Factories;
using Desktop.ViewModels.Chats;
using Desktop.ViewModels.Dialog;
using Shared.Dto.Online;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Desktop.ViewModels;

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
        _globalHub.UserStatusChanged += OnUserStatusChanged;

        InitializeSearchManager();
        _ = LoadChats().ContinueWith(
            t => Debug.WriteLine($"[ChatsVM] Initial load failed: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void InitializeSearchManager()
    {
        if (!_authManager.Session.UserId.HasValue) return;

        SearchManager = new GlobalSearchManager(_authManager.Session.UserId.Value, IsGroupMode, _apiClient, getUsersFunc: LoadUsersForFilterAsync, getChatsFunc: LoadChatsForFilterAsync);

        SearchManager.PropertyChanged += OnSearchManagerPropertyChanged;
    }

    private async Task<List<SearchFilterItem>> LoadUsersForFilterAsync()
    {
        var result = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        if (!result.Success || result.Data == null) return [];

        return result.Data.ConvertAll(u => new SearchFilterItem(u.Id, u.DisplayName ?? u.Username ?? string.Empty, u.Avatar));
    }

    private async Task<List<SearchFilterItem>> LoadChatsForFilterAsync()
    {
        var userId = _authManager.Session.UserId ?? 0;
        var result = await _apiClient.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(userId));
        if (!result.Success || result.Data == null) return [];

        return result.Data.ConvertAll(c => new SearchFilterItem(c.Id, c.Name ?? string.Empty, c.Avatar));
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
        }
    }

    private void SyncSearchScopeWithChatViewMode()
    {
        if (SearchManager == null) return;

        if (CurrentChatViewModel?.IsSearchMode == true && SelectedChat != null && CurrentChatViewModel.Chat?.Id == SelectedChat.Id)
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
    private void SetSearchScope(SearchScopeMode scope) => SearchManager?.UseScope(scope);

    [RelayCommand]
    private async Task OpenSearchFilters()
    {
        if (SearchManager == null) return;
        SearchManager.EnterSearchMode();

        var dialog = new SearchFiltersDialogViewModel(SearchManager, applyAction: () => SearchManager.ApplyFiltersAsync(), clearAction: () => SearchManager.ApplyFiltersAsync());

        await Parent.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private void SetSearchContentFilter(SearchContentFilter filter)
    {
        if (SearchManager == null) return;
        SearchManager.ContentFilter = filter;
    }

    [RelayCommand]
    private void SetSearchAuthorFilter(SearchAuthorFilter filter)
    {
        if (SearchManager == null) return;
        SearchManager.AuthorFilter = filter;
    }

    [RelayCommand]
    private async Task ApplySearchFilters()
    {
        if (SearchManager == null) return;
        await SearchManager.ApplyFiltersAsync();
    }

    [RelayCommand]
    private async Task ClearSearchFilters()
    {
        if (SearchManager == null) return;
        SearchManager.ResetFilters();
        await SearchManager.ApplyFiltersAsync();
    }


    private void OnTotalUnreadChanged(int total) => TotalUnreadCount = total;

    private void OnUnreadCountChanged(int chatId, int unreadCount) => FindChat(chatId)?.Apply(c => c.UnreadCount = unreadCount);

    private void OnMessageReceivedGlobally(MessageDto message)
    {
        var chat = FindChat(message.ChatId);
        if (chat == null) return;

        var currentUserId = _authManager.Session.UserId;
        var isDialog = chat.Type == ChatType.Contact;
        var (preview, hidePrefix) = ChatPreviewFormatter.BuildPreviewWithMeta(message, currentUserId, isDialog);

        chat.LastMessageSenderName = ChatPreviewFormatter.FormatSenderName(message.SenderName, message.SenderId, currentUserId);
        chat.LastMessagePreview = preview;
        chat.LastMessageDate = message.CreatedAt;
        chat.HideSenderPrefix = hidePrefix;

        MoveChatToTop(chat);
    }

    private void OnUserStatusChanged(UserStatusDto status)
    {
        foreach (var chat in Chats)
        {
            if (chat.Type == ChatType.Contact && chat.ContactUserId == status.UserId)
            {
                chat.ContactIsOnline = status.IsOnline;
                chat.ContactStatusType = status.StatusType;
                chat.ContactStatusExpiresAt = status.StatusExpiresAt;
            }
        }
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

    public async Task OpenOrCreateDialogWithUserAsync(UserDto user) => await SafeExecuteAsync(async () =>
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
    [RelayCommand]
    private void ToggleSortOrder() => SearchManager?.ToggleSortOrder();
    partial void OnSelectedChatChanged(ChatListItemViewModel? value)
    {
        MemoryDiagnostics.Dump($"ChatSelected: {value?.Name ?? "null"}");

        SyncSearchScopeWithChatViewMode();
        SetSearchChatContext(value);

        if (value == null) return;

        if (value.UnreadCount > 0)
        {
            value.UnreadCount = 0;
            _ = _globalHub.MarkChatAsReadAsync(value.Id);
        }

        if (CurrentChatViewModel?.Chat?.Id != value.Id)
            CurrentChatViewModel = _chatViewModelFactory.Create(value.ToDto(), this);
    }

    partial void OnCurrentChatViewModelChanged(ChatViewModel? oldValue, ChatViewModel? newValue)
    {
        if (oldValue != null)
            MemoryDiagnostics.Dump($"ChatVM disposing START: chatId={oldValue.Context?.ChatId}"); // <-- ДАМП

        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged -= OnChatVmPropertyChanged;

        _subscribedChatVm = newValue;

        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged += OnChatVmPropertyChanged;

        SyncSearchScopeWithChatViewMode();
        OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));

        if (newValue != null)
            MemoryDiagnostics.Dump($"ChatVM created: chatId={newValue.Context?.ChatId}"); // <-- ДАМП

        if (oldValue != null && !ReferenceEquals(oldValue, newValue))
        {
            var vmToDispose = oldValue;
            _ = Task.Run(async () =>
            {
                try
                {
                    await vmToDispose.DisposeAsync();

                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);

                    MemoryDiagnostics.DumpDetailed($"After ChatVM dispose+GC chatId={vmToDispose.Context?.ChatId}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChatsVM] ChatVM dispose error: {ex.Message}");
                }
            });
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

                IsInitialLoading = false;

                await LoadFreshChatsAsync(_authManager.Session.UserId.Value);
            });
        }
        finally
        {
            _isFirstLoad = false;
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

        ApplyClientSideTransforms(ordered, userId);

        foreach (var c in ordered)
            c.UnreadCount = _globalHub.GetUnreadCount(c.Id);

        MergeChats(ordered);

        TotalUnreadCount = _globalHub.GetTotalUnread();

        if (SelectedChat != null)
        {
            var restored = FindChat(SelectedChat.Id);
            if (restored != null && !ReferenceEquals(restored, SelectedChat))
                SelectedChat = restored;
        }

        await SaveCacheSilentAsync(ordered);
    }
    private void MergeChats(List<ChatDto> fresh)
    {
        var freshIds = fresh.Select(c => c.Id).ToHashSet();

        for (var i = Chats.Count - 1; i >= 0; i--)
        {
            if (!freshIds.Contains(Chats[i].Id))
                Chats.RemoveAt(i);
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            var dto = fresh[i];
            var existing = Chats.FirstOrDefault(c => c.Id == dto.Id);

            if (existing != null)
            {
                existing.Apply(dto);

                var currentIdx = Chats.IndexOf(existing);
                if (currentIdx != i)
                    Chats.Move(currentIdx, Math.Min(i, Chats.Count - 1));
            }
            else
            {
                Chats.Insert(Math.Min(i, Chats.Count), new ChatListItemViewModel(dto));
            }
        }
    }

    public void UpdateChatInList(ChatDto updatedChat)
    {
        for (var i = 0; i < Chats.Count; i++)
        {
            if (Chats[i].Id != updatedChat.Id) continue;

            Chats[i].Apply(updatedChat);
            break;
        }

        if (SelectedChat?.Id == updatedChat.Id)
        {
            SelectedChat?.Apply(updatedChat);
        }
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

    private static void ApplyClientSideTransforms(List<ChatDto> chats, int currentUserId)
    {
        foreach (var chat in chats)
        {
            if (chat.Type == ChatType.Contact)
            {
                chat.LastMessageSenderName = null;
                chat.HideSenderPrefix = true;
            }
            else
            {
                if (chat.LastMessageSenderId == currentUserId && !chat.LastMessageIsSystem)
                {
                    chat.LastMessageSenderName = "Вы";
                }
                else if (!string.IsNullOrWhiteSpace(chat.LastMessageSenderName))
                {
                    var parts = chat.LastMessageSenderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    chat.LastMessageSenderName = parts[^1];
                }

                chat.HideSenderPrefix = chat.LastMessageIsSystem || chat.LastMessageIsPoll;
            }
        }
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
            _globalHub.UserStatusChanged -= OnUserStatusChanged;

            if (SearchManager != null)
            {
                SearchManager.PropertyChanged -= OnSearchManagerPropertyChanged;
                (SearchManager as IDisposable)?.Dispose();
                SearchManager = null;
            }

            _subscribedChatVm?.PropertyChanged -= OnChatVmPropertyChanged;

            var vmToDispose = CurrentChatViewModel;
            CurrentChatViewModel = null;

            if (vmToDispose != null)
            {
                _ = vmToDispose.DisposeAsync().AsTask().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.WriteLine($"[ChatsVM] Final dispose error: {t.Exception?.GetBaseException().Message}");
                }, TaskScheduler.Default);
            }
        }
        _disposed = true;
        base.Dispose(disposing);
    }
}

file static class ObjectExtensions
{
    public static void Apply<T>(this T obj, Action<T> action) => action(obj);
}