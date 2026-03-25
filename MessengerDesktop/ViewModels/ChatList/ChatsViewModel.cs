using MessengerDesktop.Data.Repositories;
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

    public UserProfileDialogViewModel? UserProfileDialog
    {
        get => CurrentChatViewModel?.UserProfileDialog;
        set => CurrentChatViewModel?.UserProfileDialog = value;
    }

    [ObservableProperty]
    private bool _isGroupMode;

    [ObservableProperty]
    private bool _isInitialLoading = true;

    public MainMenuViewModel Parent { get; }

    [ObservableProperty]
    private ObservableCollection<ChatListItemViewModel> chats = [];

    [ObservableProperty]
    private ChatListItemViewModel? selectedChat;

    [ObservableProperty]
    private ChatViewModel? currentChatViewModel;

    [ObservableProperty]
    private GlobalSearchManager? searchManager;

    [ObservableProperty]
    private int totalUnreadCount;

    public bool IsSearchMode => SearchManager?.IsSearchMode is true;

    public ChatsViewModel(MainMenuViewModel parent, bool isGroupMode, IApiClientService apiClient, IAuthManager authManager,
        IChatViewModelFactory chatViewModelFactory, IGlobalHubConnection globalHub, ILocalCacheService cacheService)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _isGroupMode = isGroupMode;
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatViewModelFactory = chatViewModelFactory ?? throw new ArgumentNullException(nameof(chatViewModelFactory));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

        _globalHub.TotalUnreadChanged += OnTotalUnreadChanged;
        _globalHub.UnreadCountChanged += OnUnreadCountChanged;
        _globalHub.MessageReceivedGlobally += OnMessageReceivedGlobally;

        InitializeSearchManager();

        _ = LoadChats().ContinueWith(t =>
        {
            if (t.Exception != null)
                Debug.WriteLine($"[ChatsVM] Initial load failed: {t.Exception}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void InitializeSearchManager()
    {
        if (_authManager.Session.UserId.HasValue)
        {
            SearchManager = new GlobalSearchManager(_authManager.Session.UserId.Value, _apiClient);
            SearchManager.PropertyChanged += OnSearchManagerPropertyChanged;
        }
    }

    private void OnSearchManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalSearchManager.IsSearchMode))
            OnPropertyChanged(nameof(IsSearchMode));

        if (e.PropertyName == nameof(GlobalSearchManager.IsChatLocalMode))
            OnPropertyChanged(nameof(IsChatLocalSearchMode));
    }

    public bool IsChatLocalSearchMode => SearchManager?.IsChatLocalMode is true;

    #region Unread Count Handlers

    private void OnTotalUnreadChanged(int total) => TotalUnreadCount = total;

    private void OnUnreadCountChanged(int chatId, int unreadCount)
    {
        var chat = Chats.FirstOrDefault(c => c.Id == chatId);
        chat?.UnreadCount = unreadCount;
    }

    private void OnMessageReceivedGlobally(MessageDto message)
    {
        var chat = Chats.FirstOrDefault(c => c.Id == message.ChatId);
        if (chat == null) return;

        var currentUserId = _authManager.Session.UserId;
        chat.LastMessageSenderName = (currentUserId.HasValue && message.SenderId == currentUserId.Value)
            ? "Вы" : message.SenderName;
        chat.LastMessagePreview = BuildLastMessagePreview(message);
        chat.LastMessageDate = message.CreatedAt;

        MoveChatToTop(chat);
    }

    private void MoveChatToTop(ChatListItemViewModel chat)
    {
        var currentIndex = Chats.IndexOf(chat);
        if (currentIndex <= 0) return;

        Chats.Move(currentIndex, 0);

        if (SelectedChat?.Id == chat.Id)
            SelectedChat = chat;
    }

    private static string BuildLastMessagePreview(MessageDto message)
    {
        if (message.Poll != null) return "Опрос";
        if (message.IsVoiceMessage) return "Голосовое сообщение";
        if (message.Files.Count > 0 && string.IsNullOrWhiteSpace(message.Content))
            return "Вложение";
        if (string.IsNullOrWhiteSpace(message.Content)) return "Сообщение";
        return message.Content.Length > 100 ? message.Content[..100] + "..." : message.Content;
    }

    #endregion

    private bool IsChatMatchingCurrentTab(ChatType chatType)
    {
        if (IsGroupMode)
            return chatType == ChatType.Chat || chatType == ChatType.Department;
        return chatType == ChatType.Contact;
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

        var existingChat = Chats.FirstOrDefault(c => c.Id == chat.Id);
        if (existingChat != null)
        {
            SelectedChat = existingChat;
        }
        else
        {
            Chats.Insert(0, chat);
            SelectedChat = chat;
        }

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
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия: {ex.Message}";
        }
    }

    public async Task OpenChatByIdAsync(int chatId, int? scrollToMessageId = null)
    {
        if (Chats.Count == 0)
            await LoadChats();

        var chat = Chats.FirstOrDefault(c => c.Id == chatId);

        if (chat == null)
        {
            var result = await _apiClient.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(chatId));
            if (result.Success && result.Data != null)
            {
                result.Data.UnreadCount = _globalHub.GetUnreadCount(chatId);
                chat = new ChatListItemViewModel(result.Data);
                Chats.Insert(0, chat);
            }
        }

        if (chat != null)
        {
            SelectedChat = chat;

            if (scrollToMessageId.HasValue && CurrentChatViewModel != null)
            {
                await CurrentChatViewModel.WaitForInitializationAsync();
                await CurrentChatViewModel.ScrollToMessageAsync(scrollToMessageId.Value);
            }
        }
    }

    [RelayCommand]
    private void CloseSearch()
    {
        SearchManager?.ExitSearch();

        if (CurrentChatViewModel?.IsSearchMode == true)
            CurrentChatViewModel.IsSearchMode = false;
    }

    [RelayCommand]
    private void OpenChat(ChatListItemViewModel? chat)
    {
        if (chat == null) return;

        try
        {
            SelectedChat = chat;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия чата: {ex.Message}";
        }
    }

    partial void OnSelectedChatChanged(ChatListItemViewModel? value)
    {
        SyncSearchScopeWithChatViewMode();

        if (SearchManager != null)
        {
            SearchManager.ChatLocalSearchChatId = value?.Id;
            SearchManager.ChatLocalSearchChatType = value?.Type;
            SearchManager.ChatLocalSearchChatName = value?.Name;
            SearchManager.ChatLocalSearchChatAvatar = value?.Avatar;
        }

        if (value != null)
        {
            if (value.UnreadCount > 0)
            {
                value.UnreadCount = 0;
                _ = _globalHub.MarkChatAsReadAsync(value.Id);
            }

            if (CurrentChatViewModel == null || CurrentChatViewModel.Chat?.Id != value.Id)
            {
                CurrentChatViewModel = _chatViewModelFactory.Create(value.Id, this);
            }
        }
    }

    partial void OnCurrentChatViewModelChanged(ChatViewModel? value)
    {
        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged -= SubscribedChatVm_PropertyChanged;

        _subscribedChatVm = value;

        if (_subscribedChatVm != null)
            _subscribedChatVm.PropertyChanged += SubscribedChatVm_PropertyChanged;

        SyncSearchScopeWithChatViewMode();
        OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
    }

    private void SyncSearchScopeWithChatViewMode()
    {
        if (SearchManager == null) return;

        var shouldUseChatLocal = CurrentChatViewModel?.IsSearchMode == true
            && SelectedChat != null && CurrentChatViewModel.Chat?.Id == SelectedChat.Id;

        if (shouldUseChatLocal)
        {
            SearchManager.ChatLocalSearchChatId = SelectedChat!.Id;
            SearchManager.ChatLocalSearchChatType = SelectedChat.Type;
            SearchManager.ChatLocalSearchChatName = SelectedChat.Name;
            SearchManager.ChatLocalSearchChatAvatar = SelectedChat.Avatar;
            return;
        }

        SearchManager.ChatLocalSearchChatId = null;
        SearchManager.ChatLocalSearchChatType = null;
        SearchManager.ChatLocalSearchChatName = null;
        SearchManager.ChatLocalSearchChatAvatar = null;
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

                SelectedChat = Chats.FirstOrDefault(c => c.Id == createdChat.Id) ?? item;
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка создания группы: {ex.Message}";
        }
    }

    private void SubscribedChatVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsInfoPanelOpen))
            OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));

        if (e.PropertyName == nameof(ChatViewModel.IsSearchMode))
        {
            SyncSearchScopeWithChatViewMode();

            if (SearchManager == null) return;

            if (CurrentChatViewModel?.IsSearchMode == true)
                SearchManager.EnterSearchMode();
            else
                SearchManager.ExitSearch();
        }
    }

    public bool CombinedIsInfoPanelVisible
        => CurrentChatViewModel?.IsInfoPanelOpen == true;

    public async Task OpenOrCreateDialogWithUserAsync(UserDto user)
    {
        await SafeExecuteAsync(async () =>
        {
            await LoadChats();

            var existingChat = await FindDialogWithUser(user.Id);

            if (existingChat != null)
            {
                var existingItem = Chats.FirstOrDefault(c => c.Id == existingChat.Id)
                    ?? new ChatListItemViewModel(existingChat);

                if (!Chats.Any(c => c.Id == existingItem.Id))
                    Chats.Insert(0, existingItem);

                OpenChatCommand.Execute(existingItem);
                return;
            }

            var userId = _authManager.Session.UserId ?? 0;

            var result = await _apiClient.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chats.Create, new ChatDto
            {
                Name = user.Id.ToString(),
                Type = ChatType.Contact,
                CreatedById = userId
            });

            if (result.Success && result.Data != null)
            {
                result.Data.Name = user.DisplayName ?? user.Username;
                result.Data.Avatar = user.Avatar;

                var createdChat = new ChatListItemViewModel(result.Data);
                Chats.Add(createdChat);
                OpenChatCommand.Execute(createdChat);
            }
            else
            {
                ErrorMessage = $"Ошибка создания диалога: {result.Error}";
            }
        });
    }

    private async Task<ChatDto?> FindDialogWithUser(int contactUserId)
    {
        var currentUserId = _authManager.Session.UserId ?? 0;
        var result = await _apiClient.GetAsync<ChatDto?>
            (ApiEndpoints.Chats.UserContact(currentUserId, contactUserId));
        return result.Success ? result.Data : null;
    }

    [RelayCommand]
    private async Task LoadMoreSearchResults()
    {
        if (SearchManager != null)
            await SearchManager.LoadMoreMessagesAsync();
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

                var userId = _authManager.Session.UserId.Value;

                if (_isFirstLoad)
                    await ShowCachedChatsAsync();

                await LoadFreshChatsFromServerAsync(userId);
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
            var cachedChats = await _cacheService.GetChatsAsync(IsGroupMode);

            if (cachedChats.Count > 0)
            {
                foreach (var chat in cachedChats)
                    chat.UnreadCount = _globalHub.GetUnreadCount(chat.Id);

                Chats = new ObservableCollection<ChatListItemViewModel>
                    (cachedChats.Select(c => new ChatListItemViewModel(c)));
                TotalUnreadCount = _globalHub.GetTotalUnread();

                sw.Stop();
                Debug.WriteLine($"[ChatsVM] Showed {cachedChats.Count} cached {(IsGroupMode ? "groups" : "dialogs")} in {sw.ElapsedMilliseconds}ms");

                IsInitialLoading = false;
            }
            else
            {
                Debug.WriteLine($"[ChatsVM] No cached {(IsGroupMode ? "groups" : "dialogs")}, waiting for server");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatsVM] Cache read failed (non-critical): {ex.Message}");
        }
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
        {
            SelectedChat = Chats.FirstOrDefault(c => c.Id == updatedChat.Id)
                           ?? new ChatListItemViewModel(updatedChat);
        }
    }

    private async Task LoadFreshChatsFromServerAsync(int userId)
    {
        var endpoint = IsGroupMode
            ? ApiEndpoints.Chats.UserGroups(userId)
            : ApiEndpoints.Chats.UserDialogs(userId);

        var result = await _apiClient.GetAsync<List<ChatDto>>(endpoint);

        if (result.Success && result.Data != null)
        {
            var orderedChats = result.Data.OrderByDescending(c => c.LastMessageDate).ToList();

            foreach (var chat in orderedChats)
                chat.UnreadCount = _globalHub.GetUnreadCount(chat.Id);

            var selectedId = SelectedChat?.Id;

            Chats = new ObservableCollection<ChatListItemViewModel>(orderedChats.Select(c => new ChatListItemViewModel(c)));
            TotalUnreadCount = _globalHub.GetTotalUnread();

            if (selectedId.HasValue)
            {
                var restoredChat = Chats.FirstOrDefault(c => c.Id == selectedId.Value);
                if (restoredChat != null && SelectedChat?.Id != restoredChat.Id)
                    SelectedChat = restoredChat;
            }

            try
            {
                await _cacheService.UpsertChatsAsync(orderedChats);
                Debug.WriteLine($"[ChatsVM] Cached {orderedChats.Count} {(IsGroupMode ? "groups" : "dialogs")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsVM] Cache write failed (non-critical): {ex.Message}");
            }
        }
        else
        {
            if (Chats.Count == 0)
                ErrorMessage = $"Ошибка загрузки чатов: {result.Error}";
            else
                Debug.WriteLine($"[ChatsVM] Server unavailable, showing cached data. Error: {result.Error}");
        }
    }

    #region IDisposable

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _globalHub.TotalUnreadChanged -= OnTotalUnreadChanged;
            _globalHub.UnreadCountChanged -= OnUnreadCountChanged;
            _globalHub.MessageReceivedGlobally -= OnMessageReceivedGlobally;

            SearchManager?.PropertyChanged -= OnSearchManagerPropertyChanged;

            _subscribedChatVm?.PropertyChanged -= SubscribedChatVm_PropertyChanged;
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    #endregion
}