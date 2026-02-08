using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels.Chat;
using MessengerDesktop.ViewModels.Factories;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class ChatsViewModel : BaseViewModel, IRefreshable
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatViewModelFactory _chatViewModelFactory;
    private readonly IGlobalHubConnection _globalHub;

    private ChatViewModel? _subscribedChatVm;
    private bool _isFirstLoad = true;
    private bool _disposed;

    IAsyncRelayCommand IRefreshable.RefreshCommand => LoadChatsCommand;

    public UserProfileDialogViewModel? UserProfileDialog
    {
        get => CurrentChatViewModel?.UserProfileDialog;
        set
        {
            if (CurrentChatViewModel != null)
                CurrentChatViewModel.UserProfileDialog = value;
        }
    }

    [ObservableProperty]
    private bool _isGroupMode;

    [ObservableProperty]
    private bool _isInitialLoading = true;

    public MainMenuViewModel Parent { get; }

    [ObservableProperty]
    private ObservableCollection<ChatDTO> chats = [];

    [ObservableProperty]
    private ChatDTO? selectedChat;

    [ObservableProperty]
    private ChatViewModel? currentChatViewModel;

    [ObservableProperty]
    private GlobalSearchManager? searchManager;

    [ObservableProperty]
    private int totalUnreadCount;

    public bool IsSearchMode => SearchManager?.IsSearchMode ?? false;

    public ChatsViewModel(
        MainMenuViewModel parent,
        bool isGroupMode,
        IApiClientService apiClient,
        IAuthManager authManager,
        IChatViewModelFactory chatViewModelFactory,
        IGlobalHubConnection globalHub)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _isGroupMode = isGroupMode;
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatViewModelFactory = chatViewModelFactory ?? throw new ArgumentNullException(nameof(chatViewModelFactory));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));

        _globalHub.TotalUnreadChanged += OnTotalUnreadChanged;
        _globalHub.UnreadCountChanged += OnUnreadCountChanged;

        InitializeSearchManager();
        _ = LoadChats();
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
        {
            OnPropertyChanged(nameof(IsSearchMode));
        }
    }

    #region Unread Count Handlers

    private void OnTotalUnreadChanged(int total) => TotalUnreadCount = total;

    private void OnUnreadCountChanged(int chatId, int unreadCount)
    {
        var chat = Chats.FirstOrDefault(c => c.Id == chatId);
        if (chat != null)
        {
            chat.UnreadCount = unreadCount;
        }
    }

    #endregion

    private bool IsChatMatchingCurrentTab(ChatType chatType)
    {
        if (IsGroupMode)
        {
            return chatType == ChatType.Chat || chatType == ChatType.Department;
        }
        else
        {
            return chatType == ChatType.Contact;
        }
    }

    [RelayCommand]
    private async Task OpenSearchedChat(ChatDTO? chat)
    {
        if (chat == null) return;

        if (!IsChatMatchingCurrentTab(chat.Type))
        {
            await Parent.SwitchToTabAndOpenChatAsync(chat);
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
    private async Task OpenSearchResult(GlobalSearchMessageDTO? searchResult)
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
        {
            await LoadChats();
        }

        var chat = Chats.FirstOrDefault(c => c.Id == chatId);

        if (chat == null)
        {
            var result = await _apiClient.GetAsync<ChatDTO>(ApiEndpoints.Chat.ById(chatId));
            if (result.Success && result.Data != null)
            {
                chat = result.Data;
                chat.UnreadCount = _globalHub.GetUnreadCount(chatId);
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
    private void CloseSearch() => SearchManager?.ExitSearch();

    [RelayCommand]
    private void OpenChat(ChatDTO? chat)
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

    partial void OnSelectedChatChanged(ChatDTO? value)
    {
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

        OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
    }

    [RelayCommand]
    private async Task CreateGroup()
    {
        try
        {
            await Parent.ShowCreateGroupDialogAsync(createdChat =>
            {
                if (!Chats.Any(c => c.Id == createdChat.Id))
                {
                    Chats.Insert(0, createdChat);
                }
                SelectedChat = createdChat;
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
    }

    public bool CombinedIsInfoPanelVisible => CurrentChatViewModel?.IsInfoPanelOpen == true;

    public async Task OpenOrCreateDialogWithUserAsync(UserDTO user)
    {
        await SafeExecuteAsync(async () =>
        {
            await LoadChats();

            var existingChat = await FindDialogWithUser(user.Id);

            if (existingChat != null)
            {
                OpenChatCommand.Execute(existingChat);
                return;
            }

            var userId = _authManager.Session.UserId ?? 0;

            var result = await _apiClient.PostAsync<ChatDTO, ChatDTO>(ApiEndpoints.Chat.Create, new ChatDTO
            {
                Name = user.Id.ToString(),
                Type = ChatType.Contact,
                CreatedById = userId
            });

            if (result.Success && result.Data != null)
            {
                result.Data.Name = user.DisplayName ?? user.Username;
                result.Data.Avatar = user.Avatar;

                Chats.Add(result.Data);
                OpenChatCommand.Execute(result.Data);
            }
            else
            {
                ErrorMessage = $"Ошибка создания диалога: {result.Error}";
            }
        });
    }

    private async Task<ChatDTO?> FindDialogWithUser(int contactUserId)
    {
        var currentUserId = _authManager.Session.UserId ?? 0;

        var result = await _apiClient.GetAsync<ChatDTO?>(
            ApiEndpoints.Chat.UserContact(currentUserId, contactUserId));

        return result.Success ? result.Data : null;
    }

    [RelayCommand]
    private async Task LoadMoreSearchResults()
    {
        if (SearchManager != null)
        {
            await SearchManager.LoadMoreMessagesAsync();
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

                var userId = _authManager.Session.UserId.Value;

                var endpoint = IsGroupMode
                    ? ApiEndpoints.Chat.UserGroups(userId)
                    : ApiEndpoints.Chat.UserDialogs(userId);

                var result = await _apiClient.GetAsync<List<ChatDTO>>(endpoint);

                if (result.Success && result.Data != null)
                {
                    var orderedChats = result.Data.OrderByDescending(c => c.LastMessageDate).ToList();

                    foreach (var chat in orderedChats)
                    {
                        chat.UnreadCount = _globalHub.GetUnreadCount(chat.Id);
                    }

                    Chats = new ObservableCollection<ChatDTO>(orderedChats);
                    TotalUnreadCount = _globalHub.GetTotalUnread();
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки чатов: {result.Error}";
                }
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

    #region IDisposable

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _globalHub.TotalUnreadChanged -= OnTotalUnreadChanged;
            _globalHub.UnreadCountChanged -= OnUnreadCountChanged;

            if (SearchManager != null)
            {
                SearchManager.PropertyChanged -= OnSearchManagerPropertyChanged;
            }

            if (_subscribedChatVm != null)
            {
                _subscribedChatVm.PropertyChanged -= SubscribedChatVm_PropertyChanged;
            }
        }

        _disposed = true;

        base.Dispose(disposing);
    }

    #endregion
}