using Desktop.Infrastructure.Diagnostics;
using Desktop.Services.Features.Call;
using Desktop.Services.UI;
using Desktop.ViewModels.Call;
using Desktop.ViewModels.Chat;
using Desktop.ViewModels.Chat.Navigation;
using Desktop.ViewModels.ChatList.Factories;
using Desktop.ViewModels.Chats;
using Desktop.ViewModels.Department;
using Desktop.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using Shared.Dto.Call;
using Shared.Dto.Online;
using System.Diagnostics;

namespace Desktop.ViewModels;

public partial class MainMenuViewModel : BaseViewModel, IChatNavigator
{
    private readonly MainWindowViewModel _mainWindowVm;
    private readonly IApiClientService _api;
    private readonly IAuthManager _auth;
    private readonly IChatsViewModelFactory _chatsFactory;
    private readonly IServiceProvider _sp;
    private readonly IGlobalHubConnection _globalHub;
    private readonly ICallHubConnection _callHub;
    private readonly Stack<int> _backHistory = [], _forwardHistory = [];

    private ChatsViewModel? _chatsVm, _contactsVm;
    private DepartmentManagementViewModel? _deptVm;
    private ProfileViewModel? _profileVm;
    private AdminViewModel? _adminVm;
    private SettingsViewModel? _settingsVm;
    private CancellationTokenSource? _searchCts;
    private readonly GlobalSearchManager _searchManager;
    private readonly ActiveCallStore _activeCallStore;
    [ObservableProperty]
    public partial UserStatusType CurrentStatusType { get; set; } = UserStatusType.Online;

    [ObservableProperty]
    public partial string CurrentStatusText { get; set; } = "В сети";

    [ObservableProperty]
    public partial string CurrentStatusColor { get; set; } = "#43A047";

    [ObservableProperty]
    public partial string? CurrentStatusDuration { get; set; }
    public CallBannerViewModel CallBanner { get; }

    [ObservableProperty] public partial BaseViewModel? CurrentMenuViewModel { get; set; }
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ObservableCollection<UserDto> AllContacts { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<ChatDto> UserChats { get; set; } = [];
    [ObservableProperty] public partial bool IsSearching { get; set; }
    [ObservableProperty] public partial int SelectedMenuIndex { get; set; } = 1;
    [ObservableProperty] public partial bool IsAdminSectionVisible { get; set; }
    [ObservableProperty] public partial bool IsDepartmentSectionVisible { get; set; }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchText && !IsSearching;
    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;
    public static bool IsSearchAvailable => true;
    public GlobalSearchManager SearchManager => _searchManager;
    public bool IsSearchMode => _searchManager.IsSearchMode;

    public MainMenuViewModel(MainWindowViewModel mainWindowVm, IApiClientService api, IAuthManager auth, IChatsViewModelFactory chatsFactory,
        IServiceProvider sp, IGlobalHubConnection globalHub, ICallHubConnection callHub, ActiveCallStore activeCallStore)
    {
        _mainWindowVm = mainWindowVm ?? throw new ArgumentNullException(nameof(mainWindowVm));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _chatsFactory = chatsFactory ?? throw new ArgumentNullException(nameof(chatsFactory));
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));
        _callHub = callHub ?? throw new ArgumentNullException(nameof(callHub));
        _callHub.IncomingCall += OnIncomingCall;
        _callHub.CallStateUpdated += OnCallStateUpdated;
        _activeCallStore = activeCallStore;
        _globalHub.UserStatusChanged += OnUserStatusChanged;
        _globalHub.UserRoleUpdated += OnUserRoleUpdated;
        _globalHub.UserPermissionsChanged += OnUserPermissionsChanged;
        _globalHub.UserBanned += OnUserBanned;

        CallBanner = new CallBannerViewModel(activeCallStore, _sp.GetRequiredService<ICallService>());

        UserId = _auth.Session.UserId ?? throw new InvalidOperationException("User not authenticated");

        _searchManager = new GlobalSearchManager(UserId, true, _api,
            getUsersFunc: () => Task.FromResult(AllContacts.Select(u => new SearchFilterItem(u.Id, u.DisplayName ?? u.Username ?? string.Empty, u.Avatar)).ToList()),
            getChatsFunc: () => Task.FromResult(UserChats.Select(c => new SearchFilterItem(c.Id, c.Name ?? string.Empty, c.Avatar)).ToList()));

        _searchManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GlobalSearchManager.IsSearchMode))
                OnPropertyChanged(nameof(IsSearchMode));
        };

        _auth.Session.SessionChanged += OnSessionChanged;
        RefreshRoleBasedUi();

        CurrentMenuViewModel = _chatsVm = _chatsFactory.Create(this, isGroupMode: true);

        _ = LoadContactsAndChatsAsync();

        if (_auth.Session.IsAuthenticated)
        {
            _ = InitGlobalHubAsync();
        }
    }
    private void OnSessionChanged()
    {
        RefreshRoleBasedUi();
        if (_auth.Session.IsAuthenticated && !_callHub.IsConnected)
        {
            _ = InitGlobalHubAsync();
        }
    }

    private async void OnUserRoleUpdated(UserRole role)
    {
        await _auth.UpdateRoleAsync(role);

        try
        {
            await _auth.TryRefreshTokenAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenu] Failed to refresh token after role update: {ex.Message}");
        }

        var currentIndex = SelectedMenuIndex;
        RefreshRoleBasedUi();

        if (_adminVm != null)
        {
            await _adminVm.RefreshCommand.ExecuteAsync(null);
        }

        if ((currentIndex == 4 || currentIndex == 6) && !IsAdminSectionVisible && !IsDepartmentSectionVisible)
        {
            NavigateTo(1, false);
            var notify = _sp.GetRequiredService<INotificationService>();
            notify.Show(
                "Права изменены",
                "Ваши права администратора были отозваны. Вы переведены в основной раздел.",
                DesktopNotificationType.Warning);
        }
    }

    private void RefreshRoleBasedUi()
    {
        IsAdminSectionVisible = _auth.Session.IsAdmin;
        IsDepartmentSectionVisible = _auth.Session.IsHead;

        if ((SelectedMenuIndex == 4 && !IsAdminSectionVisible) ||
            (SelectedMenuIndex == 6 && !IsDepartmentSectionVisible))
            NavigateTo(1, false);
    }

    private async Task InitGlobalHubAsync()
    {
        Debug.WriteLine("[MainMenu] InitGlobalHubAsync START");
        try
        {
            await _globalHub.DisconnectAsync();
            await _globalHub.ConnectAsync();
            Debug.WriteLine($"[MainMenu] GlobalHub connected: {_globalHub.IsConnected}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenu] Failed to connect global hub: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            _callHub.IncomingCall -= OnIncomingCall;
            _callHub.CallStateUpdated -= OnCallStateUpdated;

            await _callHub.DisconnectAsync();
            await _callHub.ConnectAsync();

            _callHub.IncomingCall += OnIncomingCall;
            _callHub.CallStateUpdated += OnCallStateUpdated;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to connect call hub: {ex.Message}");
        }
    }

    private void OnIncomingCall(CallInviteDto invite) => Dispatcher.UIThread.Post(async () =>
    {
        try
        {
            // Проверяем подключение с небольшим ожиданием
            if (!_callHub.IsConnected)
            {
                Debug.WriteLine("[MainMenuViewModel] IncomingCall: CallHub не подключён, ожидание...");
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!_callHub.IsConnected && DateTime.UtcNow < deadline)
                    await Task.Delay(100);

                if (!_callHub.IsConnected)
                {
                    Debug.WriteLine("[MainMenuViewModel] IncomingCall: CallHub так и не подключился, пропуск");
                    return;
                }
            }

            // Если уже в звонке — показываем баннер вместо диалога
            if (_activeCallStore.IsInCall)
            {
                Debug.WriteLine("[MainMenuViewModel] IncomingCall: уже в звонке, пропуск диалога");
                return;
            }

            var callService = _sp.GetRequiredService<ICallService>();
            var vm = new IncomingCallViewModel(callService, invite);
            vm.Accepted += acceptedInvite => _ = OnCallAcceptedAsync(acceptedInvite);
            await _mainWindowVm.ShowDialogAsync(vm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenuViewModel] IncomingCall popup error: {ex.Message}");
        }
    });

    private async Task OnCallAcceptedAsync(CallInviteDto invite)
    {
        try
        {
            var callService = _sp.GetRequiredService<ICallService>();

            string? joinError = null;
            void OnError(string msg) => joinError = msg;
            _callHub.CallError += OnError;

            CallStateDto? receivedState = null;
            var stateTcs = new TaskCompletionSource<CallStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnStateUpdated(CallStateDto s)
            {
                if (s.CallId == invite.CallId)
                    stateTcs.TrySetResult(s);
            }

            _callHub.CallStateUpdated += OnStateUpdated;

            await callService.JoinCallAsync(invite.CallId, invite.ChatId);

            _callHub.CallError -= OnError;

            if (joinError != null)
            {
                _callHub.CallStateUpdated -= OnStateUpdated;
                Debug.WriteLine($"[MainMenuViewModel] JoinCall вернул ошибку: {joinError}");
                return;
            }

            using var cts = new CancellationTokenSource(3000);

            try
            {
                receivedState = await stateTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                receivedState = await _callHub.GetCallStateAsync(invite.ChatId);
            }
            finally
            {
                _callHub.CallStateUpdated -= OnStateUpdated;
            }

            var state = receivedState ?? new CallStateDto
            {
                CallId = invite.CallId,
                ChatId = invite.ChatId,
                InitiatorId = invite.InitiatorId,
                IsGroupCall = invite.IsGroupCall,
                StartedAt = DateTimeOffset.UtcNow,
                Participants = []
            };

            var myUserId = _auth.Session.UserId ?? 0;
            if (myUserId > 0 && state.Participants.All(p => p.UserId != myUserId))
            {
                state.Participants.Add(new CallParticipantDto
                {
                    UserId = myUserId,
                    DisplayName = "Вы"
                });
            }

            var chatName = UserChats.FirstOrDefault(c => c.Id == invite.ChatId)?.Name
                           ?? invite.ChatName;

            var openChatTask = OpenCallChatAsync(invite);
            Dispatcher.UIThread.Post(() => ShowCallViewSync(state, chatName, invite.IsGroupCall));
            await openChatTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenuViewModel] OnCallAccepted error: {ex.Message}");
        }
    }

    private void ShowCallViewSync(CallStateDto state, string chatName, bool isGroupCall)
    {
        if (_activeCallStore.ActiveCall != null)
        {
            _activeCallStore.OpenCallUi();
            return;
        }

        var callService = _sp.GetRequiredService<ICallService>();
        var audioService = _sp.GetRequiredService<CallAudioService>();
        var callVm = new CallViewModel(callService, _callHub, _activeCallStore, audioService);
        callVm.Initialize(state, chatName, isGroupCall, _auth.Session.UserId ?? 0);

        _activeCallStore.ActiveCall = callVm;
        _activeCallStore.OpenCallUi();
    }

    private void OnCallStateUpdated(CallStateDto state)
    {
        if (_activeCallStore.IsInCall) return;

        var myUserId = _auth.Session.UserId ?? 0;
        if (state.InitiatorId != myUserId) return;

        var callService = _sp.GetRequiredService<ICallService>();

        if (callService.ActiveChatId != state.ChatId) return;

        var chatName = UserChats.FirstOrDefault(c => c.Id == state.ChatId)?.Name ?? string.Empty;

        Dispatcher.UIThread.Post(() => ShowCallViewSync(state, chatName, state.IsGroupCall));
    }

    private void OnUserStatusChanged(UserStatusDto status)
    {
        if (status.UserId != UserId) return;

        CurrentStatusType = status.StatusType;
        CurrentStatusText = status.IsOnline ? status.StatusType switch
        {
            UserStatusType.Online => "В сети",
            UserStatusType.Away => "Отошёл",
            UserStatusType.Busy => "Занят",
            UserStatusType.DoNotDisturb => "Не беспокоить",
            _ => "В сети"
        } : "Не в сети";

        CurrentStatusColor = status.IsOnline ? status.StatusType switch
        {
            UserStatusType.Online => "#43A047",
            UserStatusType.Away => "#FFA000",
            UserStatusType.Busy => "#E53935",
            UserStatusType.DoNotDisturb => "#9C27B0",
            _ => "#43A047"
        } : "#9E9E9E";
    }

    private async Task OpenCallChatAsync(CallInviteDto invite)
    {
        try
        {
            var chat = UserChats.FirstOrDefault(c => c.Id == invite.ChatId);
            if (chat == null)
            {
                var r = await _api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(invite.ChatId));
                if (r is { Success: true, Data: not null })
                {
                    chat = r.Data;
                    if (UserChats.All(c => c.Id != chat.Id))
                        UserChats.Insert(0, chat);
                }
                else
                {
                    Debug.WriteLine($"[MainMenuViewModel] Failed to load chat {invite.ChatId}");
                    return;
                }
            }

            await SwitchToTabAndOpenChatAsync(chat);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenuViewModel] OpenCallChat error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenSearchFilters()
    {
        if (SearchManager == null) return;
        SearchManager.EnterSearchMode();

        var dialog = new SearchFiltersDialogViewModel(SearchManager, () => SearchManager.ApplyFiltersAsync(), () => SearchManager.ApplyFiltersAsync());

        await ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private void ClearSenderFilter()
    {
        SearchManager.SelectedSender = null;
        SearchManager.SenderSearchText = string.Empty;
    }

    [RelayCommand]
    private void ClearChatFilter()
    {
        SearchManager.SelectedChatFilter = null;
        SearchManager.ChatSearchText = string.Empty;
    }

    [RelayCommand]
    private void ClearContentFilter() => SearchManager.ContentFilter = SearchContentFilter.Any;

    [RelayCommand]
    private void ClearDateFrom() => SearchManager.DateFromFilter = null;

    [RelayCommand]
    private void ClearDateTo() => SearchManager.DateToFilter = null;

    [RelayCommand]
    private async Task ClearAllFilters()
    {
        SearchManager.ResetFilters();
        await SearchManager.ApplyFiltersAsync();
    }

    #region Navigation

    [RelayCommand]
    private void SetItem(int index) => NavigateTo(index, addToHistory: true);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!CanGoBack) return;
        var prev = _backHistory.Pop();
        if (SelectedMenuIndex != prev) _forwardHistory.Push(SelectedMenuIndex);
        NavigateTo(prev, false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (!CanGoForward) return;
        var next = _forwardHistory.Pop();
        if (SelectedMenuIndex != next) _backHistory.Push(SelectedMenuIndex);
        NavigateTo(next, false);
    }

    public void SetActiveMenu(int index) => NavigateTo(index, true);

    private bool HasAccessToMenu(int index) => index switch
    {
        4 => IsAdminSectionVisible,
        6 => IsDepartmentSectionVisible,
        _ => true
    };

    private void NavigateTo(int index, bool addToHistory)
    {
        if (!HasAccessToMenu(index))
        {
            if (SelectedMenuIndex is 4 or 6)
                index = 1;
            else return;
        }

        if (addToHistory && SelectedMenuIndex != index)
        {
            _backHistory.Push(SelectedMenuIndex);
            _forwardHistory.Clear();
        }

        static bool IsChatTab(int i) => i is 1 or 2 or 5;
        if (IsChatTab(SelectedMenuIndex) && !IsChatTab(index))
        {
            ResetChat(_chatsVm);
            ResetChat(_contactsVm);
        }

        SelectedMenuIndex = index;
        ClearSearch();

        CurrentMenuViewModel = index switch
        {
            0 => _settingsVm ??= _sp.GetRequiredService<SettingsViewModel>(),
            1 or 2 => _chatsVm ??= _chatsFactory.Create(this, true),
            3 => _profileVm ??= _sp.GetRequiredService<ProfileViewModel>(),
            4 => _adminVm ??= _sp.GetRequiredService<AdminViewModel>(),
            5 => _contactsVm ??= _chatsFactory.Create(this, false),
            6 => GetOrCreateDeptVm(),
            _ => CurrentMenuViewModel
        };

        NotifyNavState();
        MemoryDiagnostics.Dump($"NavigateTo index={index} ({GetMenuName(index)})");
    }
    private static string GetMenuName(int i) => i switch
    {
        0 => "Settings",
        1 or 2 => "Chats",
        3 => "Profile",
        4 => "Admin",
        5 => "Contacts",
        6 => "Departments",
        _ => "Unknown"
    };

    private static void ResetChat(ChatsViewModel? vm)
    {
        if (vm?.CurrentChatViewModel == null) return;
        vm.SelectedChat = null;
        vm.CurrentChatViewModel = null;
    }

    private void NotifyNavState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private DepartmentManagementViewModel GetOrCreateDeptVm()
    {
        if (_deptVm != null) return _deptVm;

        _deptVm = _sp.GetRequiredService<DepartmentManagementViewModel>();
        _deptVm.OpenChatWithUserAction = async u => await OpenOrCreateChatAsync(u);
        _deptVm.NavigateToChatAction = async id =>
        {
            var chat = UserChats.FirstOrDefault(c => c.Id == id);
            if (chat != null) await OpenChatAsync(chat);
        };
        _deptVm.ShowRemoveConfirmAction = async member =>
        {
            var dlg = new ConfirmDialogViewModel("Удаление из отдела", $"Вы уверены, что хотите удалить {member.DisplayName} из отдела?", "Удалить", "Отмена");
            await _mainWindowVm.ShowDialogAsync(dlg);
            return await dlg.Result;
        };
        _deptVm.ShowSelectUserAction = async users =>
        {
            var picker = new UserPickerDialogViewModel("Добавить сотрудника", users);
            await _mainWindowVm.ShowDialogAsync(picker);
            return await picker.SingleSelectResult;
        };
        return _deptVm;
    }

    #endregion

    #region Search

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        OnPropertyChanged(nameof(HasSearchText));
        SearchManager.SearchQuery = value;
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowNoResults));

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchManager.Clear();
    }

    [RelayCommand]
    private void CloseSearch()
    {
        SearchManager.ExitSearch();
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void SetSearchScope(SearchScopeMode scope)
    {
        SearchManager.UseScope(scope);
        OnPropertyChanged(nameof(IsSearchMode));
    }

    [RelayCommand]
    private async Task OpenSearchedChat(ChatListItemViewModel? chat)
    {
        if (chat == null) return;
        await SwitchToTabAndOpenChatAsync(chat.ToDto());
        CloseSearch();
    }

    [RelayCommand]
    private async Task OpenSearchResult(GlobalSearchMessageDto? message)
    {
        if (message == null) return;
        await SwitchToTabAndOpenMessageAsync(message);
        CloseSearch();
    }

    [RelayCommand]
    private async Task LoadMoreSearchResults() => await SearchManager.LoadMoreMessagesAsync();

    #endregion

    #region Chat open
    public async Task NavigateToForwardedChatAsync(ChatDto targetChat)
    {
        bool isGroup = targetChat.Type is not ChatType.Contact;
        SetActiveMenu(isGroup ? 1 : 5);
        await Task.Delay(50);

        var vm = isGroup
            ? (_chatsVm ??= _chatsFactory.Create(this, true))
            : (_contactsVm ??= _chatsFactory.Create(this, false));

        CurrentMenuViewModel = vm;

        if (vm.Chats.All(c => c.Id != targetChat.Id))
            vm.Chats.Insert(0, new ChatListItemViewModel(targetChat));

        vm.SelectedChat = vm.Chats.FirstOrDefault(c => c.Id == targetChat.Id);

        if (vm.CurrentChatViewModel != null)
        {
            await vm.CurrentChatViewModel.WaitForInitializationAsync();
            vm.CurrentChatViewModel.RequestScrollToBottom();
        }
    }

    public async Task SwitchToTabAndOpenChatAsync(ChatDto chat)
    {
        if (chat.Type is not ChatType.Contact)
        {
            await OpenChatAsync(chat);
            return;
        }
        _contactsVm ??= _chatsFactory.Create(this, false);
        await EnsureAndSelectChatAsync(_contactsVm, chat, 5);
    }

    public async Task SwitchToTabAndOpenMessageAsync(GlobalSearchMessageDto msg)
    {
        bool isGroup = msg.ChatType is not ChatType.Contact;
        SetActiveMenu(isGroup ? 1 : 5);
        await Task.Delay(50);

        var vm = isGroup
            ? (_chatsVm ??= _chatsFactory.Create(this, true))
            : (_contactsVm ??= _chatsFactory.Create(this, false));

        CurrentMenuViewModel = vm;
        await vm.OpenChatByIdAsync(msg.ChatId, msg.Id);
    }

    [RelayCommand]
    public async Task OpenOrCreateChatAsync(UserDto user)
    {
        SetActiveMenu(5);
        await Task.Delay(50);
        if (_contactsVm != null) await _contactsVm.OpenOrCreateDialogWithUserAsync(user);
    }

    private async Task OpenChatAsync(ChatDto chat)
    {
        _chatsVm ??= _chatsFactory.Create(this, true);
        await EnsureAndSelectChatAsync(_chatsVm, chat, 1);
    }

    private async Task EnsureAndSelectChatAsync(ChatsViewModel vm, ChatDto chat, int menuIndex)
    {
        SetActiveMenu(menuIndex);
        await Task.Delay(50);
        if (vm.Chats.All(c => c.Id != chat.Id))
            vm.Chats.Insert(0, new ChatListItemViewModel(chat));
        vm.SelectedChat = vm.Chats.FirstOrDefault(c => c.Id == chat.Id);
        CurrentMenuViewModel = vm;
    }

    #endregion

    #region Notifications

    public async Task OpenNotificationAsync(NotificationDto notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var chat = UserChats.FirstOrDefault(c => c.Id == notification.ChatId);
        if (chat == null)
        {
            var r = await _api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(notification.ChatId));
            chat = r is { Success: true, Data: not null } ? r.Data : throw new InvalidOperationException(r.Error ?? "Не удалось загрузить чат из уведомления.");
            if (UserChats.All(c => c.Id != chat.Id)) UserChats.Insert(0, chat);
        }

        if (notification.MessageId is { } msgId)
        {
            await SwitchToTabAndOpenMessageAsync(new GlobalSearchMessageDto { Id = msgId, ChatId = notification.ChatId, ChatType = chat.Type });
            return;
        }
        await SwitchToTabAndOpenChatAsync(chat);
    }

    #endregion

    #region Dialogs

    public async Task ShowUserProfileAsync(int userId) => await SafeExecuteAsync(async () =>
    {
        var r = await _api.GetAsync<UserDto>(ApiEndpoints.Users.ById(userId));
        if (!r.Success || r.Data == null) { ErrorMessage = $"Не удалось загрузить профиль: {r.Error}"; return; }

        var dlg = new UserProfileDialogViewModel(r.Data, _api)
        {
            CanSendMessage = r.Data.Id != _auth.Session.UserId,
            OpenChatWithUserAction = async u => await OpenOrCreateChatAsync(u)
        };
        await _mainWindowVm.ShowDialogAsync(dlg);
    });

    public async Task ShowPollDialogAsync(int chatId, Func<Task>? onCreated = null)
    {
        try
        {
            await _mainWindowVm.ShowDialogAsync(new PollDialogViewModel(chatId)
            {
                CreateAction = async dto => { await CreatePollAsync(dto); if (onCreated != null) await onCreated(); }
            });
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия диалога: {ex.Message}"; }
    }

    public async Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onUpdated = null)
    {
        try
        {
            var members = (await _api.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(chat.Id))).Data;
            await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, UserId, chat, members, _auth.Session.IsAdmin)
            {
                SaveAction = async (dto, mIds, aIds, s, n, rem) => await UpdateGroupChatAsync(dto, mIds, aIds, s, n, rem, onUpdated),
                DeleteAction = DeleteGroupChatAsync,
                ShowDialogAction = vm => _mainWindowVm.ShowDialogAsync(vm)
            });
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия диалога: {ex.Message}"; }
    }

    public async Task ShowCreateGroupDialogAsync(Action<ChatDto>? onCreated = null)
    {
        try
        {
            await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, UserId, isSystemAdmin: _auth.Session.IsAdmin)
            {
                SaveAction = async (dto, mIds, aIds, s, n, _) => await CreateGroupChatAsync(dto, mIds, aIds, s, n, onCreated),
                ShowDialogAction = vm => _mainWindowVm.ShowDialogAsync(vm)
            });
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия диалога: {ex.Message}"; }
    }

    public Task ShowDialogAsync(DialogBaseViewModel dialogViewModel)
        => _mainWindowVm.ShowDialogAsync(dialogViewModel);

    #endregion

    #region API operations

    private async Task CreatePollAsync(CreatePollDto dto) => await SafeExecuteAsync(async () =>
    {
        var r = await _api.PostAsync<CreatePollDto, MessageDto>(ApiEndpoints.Polls.Create, dto);
        if (r.Success) SuccessMessage = "Опрос создан";
        else ErrorMessage = $"Ошибка создания опроса: {r.Error}";
    });

    private void OnUserPermissionsChanged(UserPermissionsChangedDto dto)
    {
        var notify = _sp.GetRequiredService<INotificationService>();
        notify.Show(
            "Права изменены",
            dto.Reason,
            DesktopNotificationType.Warning);
    }

    private void OnUserBanned(UserBannedDto dto)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var notify = _sp.GetRequiredService<INotificationService>();
            notify.Show(
                "Доступ заблокирован",
                dto.Reason,
                DesktopNotificationType.Error);

            await Task.Delay(500);
            await _auth.LogoutAsync();
            NavigateToLogin();
        });
    }

    private void NavigateToLogin()
    {
        var loginVm = _sp.GetRequiredService<LoginViewModel>();
        _mainWindowVm.CurrentViewModel = loginVm;
    }

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _api.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        var chatsTask = _api.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(UserId));
        var statusTask = _api.GetAsync<UserStatusDto>(ApiEndpoints.Users.Status(UserId));

        await Task.WhenAll(usersTask, chatsTask, statusTask);

        if (await usersTask is { Success: true, Data: { } users })
            AllContacts = new ObservableCollection<UserDto>(users.Where(u => u.Id != UserId));
        if (await chatsTask is { Success: true, Data: { } chats })
            UserChats = new ObservableCollection<ChatDto>(chats);

        var statusResult = await statusTask;
        if (statusResult is { Success: true, Data: not null })
            OnUserStatusChanged(statusResult.Data);
    });

    private async Task<bool> CreateGroupChatAsync(ChatDto chatDto, List<int> memberIds, List<int> adminIds, Stream? avatarStream, string? avatarName, Action<ChatDto>? onSuccess)
    {
        try
        {
            var cr = await _api.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chats.Create, chatDto);
            if (!cr.Success || cr.Data == null) { ErrorMessage = $"Ошибка создания группы: {cr.Error}"; return false; }

            var chat = cr.Data;
            foreach (var uid in memberIds)
                await _api.PostAsync(ApiEndpoints.Chats.Members(chat.Id), new UpdateChatMemberDto { UserId = uid });
            foreach (var aid in adminIds)
                await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chat.Id, aid, ChatRole.Admin), null!);

            if (avatarStream != null && !string.IsNullOrEmpty(avatarName))
            {
                avatarStream.Position = 0;
                var av = await _api.UploadFileAsync<AvatarResponseDto>(ApiEndpoints.Chats.Avatar(chat.Id), avatarStream, avatarName, MimeType(avatarName));
                if (av is { Success: true, Data: not null }) chat.Avatar = av.Data.AvatarUrl;
            }

            UserChats.Add(chat);
            await OpenChatAsync(chat);
            onSuccess?.Invoke(chat);
            SuccessMessage = "Группа успешно создана";
            return true;
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка: {ex.Message}"; return false; }
    }
    private async Task<bool> DeleteGroupChatAsync(int chatId)
    {
        try
        {
            var result = await _api.DeleteAsync(ApiEndpoints.Chats.ById(chatId));
            if (!result.Success)
            {
                ErrorMessage = $"Ошибка удаления группы: {result.Error}";
                return false;
            }

            SuccessMessage = "Группа удалена";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка удаления группы: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> UpdateGroupChatAsync(ChatDto chatDto, List<int> memberIds, List<int> adminIds, Stream? avatarStream,
        string? avatarName, bool avatarRemoved, Action<ChatDto>? onSuccess)
    {
        try
        {
            var ur = await _api.PutAsync<UpdateChatDto, ChatDto>(ApiEndpoints.Chats.ById(chatDto.Id),
                new UpdateChatDto { Id = chatDto.Id, Name = chatDto.Name, ChatType = ChatType.Chat });
            if (!ur.Success || ur.Data == null) { ErrorMessage = $"Ошибка обновления группы: {ur.Error}"; return false; }

            var chat = ur.Data;
            await SyncMembersAsync(chatDto.Id, memberIds, adminIds, chatDto.CreatedById);

            var (ok, url, err) = await UpdateAvatarAsync(chatDto.Id, avatarStream, avatarName, avatarRemoved);
            if (!ok) { ErrorMessage = err; return false; }
            if (url != null) chat.Avatar = url;

            var existing = UserChats.FirstOrDefault(c => c.Id == chatDto.Id);
            if (existing != null) UserChats[UserChats.IndexOf(existing)] = chat;

            onSuccess?.Invoke(chat);
            SuccessMessage = "Группа успешно обновлена";
            return true;
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка: {ex.Message}"; return false; }
    }

    private async Task SyncMembersAsync(int chatId, List<int> memberIds, List<int> adminIds, int createdById)
    {
        var current = (await _api.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(chatId))).Data ?? [];
        var curIds = current.Select(m => m.UserId).ToHashSet();
        var curAdmins = current.Where(x => x.Role is ChatRole.Admin or ChatRole.Owner).Select(x => x.UserId).ToHashSet();

        foreach (var id in memberIds.Where(id => !curIds.Contains(id)))
            await _api.PostAsync(ApiEndpoints.Chats.Members(chatId), new UpdateChatMemberDto { UserId = id });
        foreach (var id in curIds.Where(id => !memberIds.Contains(id) && id != UserId))
            await _api.DeleteAsync(ApiEndpoints.Chats.RemoveMember(chatId, id));
        foreach (var id in adminIds.Where(id => curIds.Contains(id) && !curAdmins.Contains(id)))
            await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, id, ChatRole.Admin), null!);
        foreach (var id in curAdmins.Where(id => id != createdById && !adminIds.Contains(id) && curIds.Contains(id)))
            await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, id, ChatRole.Member), null!);
    }

    private async Task<(bool Ok, string? Url, string? Error)> UpdateAvatarAsync(int chatId, Stream? stream, string? fileName, bool removed)
    {
        if (stream != null && !string.IsNullOrEmpty(fileName))
        {
            stream.Position = 0;
            var r = await _api.UploadFileAsync<AvatarResponseDto>(ApiEndpoints.Chats.Avatar(chatId), stream, fileName, MimeType(fileName));
            if (r is { Success: true, Data: not null }) return (true, r.Data.AvatarUrl, null);
        }
        if (!removed) return (true, null, null);

        var del = await _api.DeleteAsync(ApiEndpoints.Chats.Avatar(chatId));
        return del.Success ? (true, string.Empty, null) : (false, null, $"Ошибка удаления аватара: {del.Error}");
    }

    private static string MimeType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    #endregion
    [RelayCommand]
    private async Task SetStatusAsync(string param)
    {
        Debug.WriteLine($"[MainMenu] SetStatus called with param: {param}");
        var (status, duration) = param switch
        {
            "Online" => (UserStatusType.Online, null),
            "Away" => (UserStatusType.Away, null),
            "Busy" => (UserStatusType.Busy, null),
            "DnD" => (UserStatusType.DoNotDisturb, null),
            "Busy15m" => (UserStatusType.Busy, "15m"),
            "Busy30m" => (UserStatusType.Busy, "30m"),
            "Busy1h" => (UserStatusType.Busy, "1h"),
            "DnD1h" => (UserStatusType.DoNotDisturb, "1h"),
            "DnD2h" => (UserStatusType.DoNotDisturb, "2h"),
            _ => (UserStatusType.Online, null)
        };

        await _globalHub.SetStatusAsync(status, duration);
    }

    public void OpenCallUi() => _activeCallStore.OpenCallUi();

    public void ShowCallView(CallStateDto state, string chatName, bool isGroupCall)
        => Dispatcher.UIThread.Post(() => ShowCallViewSync(state, chatName, isGroupCall));

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _auth.Session.SessionChanged -= OnSessionChanged;
            _callHub.IncomingCall -= OnIncomingCall;
            _callHub.CallStateUpdated -= OnCallStateUpdated;
            _globalHub.UserStatusChanged -= OnUserStatusChanged;
            _globalHub.UserRoleUpdated -= OnUserRoleUpdated;
            _globalHub.UserPermissionsChanged -= OnUserPermissionsChanged;
            _globalHub.UserBanned -= OnUserBanned;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            DisposeVm(ref _chatsVm);
            DisposeVm(ref _contactsVm);
            DisposeVm(ref _deptVm);
            DisposeVm(ref _profileVm);
            DisposeVm(ref _adminVm);
            DisposeVm(ref _settingsVm);

            if (_globalHub is IAsyncDisposable globalAd)
                _ = SafeDisposeAsync(globalAd);

            _ = SafeDisposeAsync(_callHub);
        }
        base.Dispose(disposing);
    }

    private static void DisposeVm<T>(ref T? vm) where T : BaseViewModel { vm?.Dispose(); vm = null; }

    private static async Task SafeDisposeAsync(IAsyncDisposable d)
    {
        try { await d.DisposeAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[MainMenuViewModel] Hub dispose error: {ex.Message}"); }
    }

    #endregion
}