using Core.Dialog.ChatEdit;
using Core.Dialog.Confirm;
using Core.Dialog.Poll;
using Core.Dialog.Search;
using Core.Dialog.Shared;
using Core.Dialog.UserPicker;
using Core.Dialog.UserProfile;
using Core.Features.Admin.ViewModels;
using Core.Features.Auth.ViewModels;
using Core.Features.Call.ViewModels;
using Core.Features.Chat.ViewModels.Navigation;
using Core.Features.ChatList.ViewModels;
using Core.Features.ChatList.ViewModels.Factories;
using Core.Features.ChatList.ViewModels.Items;
using Core.Features.ChatList.ViewModels.Search;
using Core.Features.Department.ViewModels;
using Core.Features.Profile.ViewModels;
using Core.Features.Settings.ViewModels;
using Core.Features.Shell.MainMenu.Status;
using Core.Features.Shell.MainMenu.ViewModels.Chat;
using Core.Features.Shell.MainMenu.ViewModels.Hubs;
using Core.Features.Shell.MainMenu.ViewModels.Navigation;
using Core.Infrastructure.Diagnostics;
using Core.Services.Api.Abstraction;
using Core.Services.Call;
using Core.Services.Call.Abstractions;
using Core.Services.UI;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Online;
using System.Diagnostics;

namespace Core.Features.Shell.MainMenu;

public partial class MainMenuViewModel : BaseViewModel, IChatNavigator
{
    private readonly MainWindowViewModel _mainWindowVm;
    private readonly IAuthManager _auth;
    private readonly IChatListViewModelFactory _chatsFactory;
    private readonly IServiceProvider _sp;
    private readonly IApiClientService _api;
    private readonly IGlobalHubConnection _globalHub;

    private readonly NavigationOrchestrator _navigator;
    private readonly HubConnectionManager _hubManager;
    private readonly ChatOperationsService _chatOps;
    private readonly IncomingCallHandler _callHandler;

    public UserStatusViewModel Status { get; }
    public CallBannerViewModel CallBanner { get; }
    public GlobalSearchManager SearchManager { get; }

    private ChatListViewModel? _chatsVm, _contactsVm;
    private DepartmentManagementViewModel? _deptVm;
    private ProfileViewModel? _profileVm;
    private AdminViewModel? _adminVm;
    private SettingsViewModel? _settingsVm;

    [ObservableProperty] public partial BaseViewModel? CurrentMenuViewModel { get; set; }
    [ObservableProperty] public partial int SelectedMenuIndex { get; set; } = 1;
    [ObservableProperty] public partial bool IsAdminSectionVisible { get; set; }
    [ObservableProperty] public partial bool IsDepartmentSectionVisible { get; set; }
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ObservableCollection<UserDto> AllContacts { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<ChatDto> UserChats { get; set; } = [];

    public string CurrentStatusColor => Status.CurrentStatusColor;

    public bool CanGoBack => _navigator.CanGoBack;
    public bool CanGoForward => _navigator.CanGoForward;
    public bool IsSearchMode => SearchManager.IsSearchMode;

    public MainMenuViewModel(
        MainWindowViewModel mainWindowVm,
        IApiClientService api,
        IPlatformService platformService,
        IAuthManager auth,
        IChatListViewModelFactory chatsFactory,
        IServiceProvider sp,
        IGlobalHubConnection globalHub,
        ICallHubConnection callHub,
        ActiveCallStore activeCallStore,
        IDrawerService drawerService)
    {
        _mainWindowVm = mainWindowVm ?? throw new ArgumentNullException(nameof(mainWindowVm));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _chatsFactory = chatsFactory ?? throw new ArgumentNullException(nameof(chatsFactory));
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _globalHub = globalHub ?? throw new ArgumentNullException(nameof(globalHub));

        UserId = auth.Session.UserId ?? throw new InvalidOperationException("User not authenticated");

        _navigator = new NavigationOrchestrator(drawerService);
        _navigator.IndexChanged += OnNavigationIndexChanged;

        _hubManager = new HubConnectionManager(globalHub, callHub);

        _chatOps = new ChatOperationsService(api, UserId);

        Status = new UserStatusViewModel(globalHub, UserId);
        Status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserStatusViewModel.CurrentStatusColor))
                OnPropertyChanged(nameof(CurrentStatusColor));
        };

        CallBanner = new CallBannerViewModel(activeCallStore, sp.GetRequiredService<ICallService>());

        _callHandler = new IncomingCallHandler(
            callHub,
            sp.GetRequiredService<ICallService>(),
            activeCallStore,
            auth,
            vm => _mainWindowVm.ShowDialogAsync(vm),
            (state, name, isGroup) =>
            {
                ShowCallViewSync(state, name, isGroup, activeCallStore, sp);
                return Task.CompletedTask;
            },
            chatId => UserChats.FirstOrDefault(c => c.Id == chatId));

        SearchManager = new GlobalSearchManager(UserId, true, api, getUsersFunc: () => Task.FromResult(
            AllContacts.Select(u => new SearchFilterItem(u.Id, u.DisplayName ?? u.Username ?? string.Empty, u.Avatar)).ToList()),
            getChatsFunc: () => Task.FromResult(UserChats.Select(c => new SearchFilterItem(c.Id, c.Name ?? string.Empty, c.Avatar)).ToList()));

        SearchManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GlobalSearchManager.IsSearchMode))
                OnPropertyChanged(nameof(IsSearchMode));
        };

        globalHub.UserRoleUpdated += OnUserRoleUpdated;
        globalHub.UserPermissionsChanged += OnUserPermissionsChanged;
        globalHub.UserBanned += OnUserBanned;
        auth.Session.SessionChanged += OnSessionChanged;

        RefreshRoleBasedUi();

        CurrentMenuViewModel = _chatsVm = _chatsFactory.Create(this, isGroupMode: true);

        _ = InitAsync();
    }

    #region Init

    private async Task InitAsync()
    {
        await _hubManager.ReconnectAllAsync();
        await LoadContactsAndChatsAsync();
    }

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _api.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        var chatsTask = _api.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(UserId));
        var statusTask = _api.GetAsync<UserStatusDto>(ApiEndpoints.Users.Status(UserId));

        await Task.WhenAll(usersTask, chatsTask, statusTask);

        if (await usersTask is { Success: true, Data: { } users }) AllContacts = new ObservableCollection<UserDto>(users.Where(u => u.Id != UserId));

        if (await chatsTask is { Success: true, Data: { } chats }) UserChats = new ObservableCollection<ChatDto>(chats);

        if (await statusTask is { Success: true, Data: { } status })
            Status.ApplyDto(status);
    });

    #endregion

    #region Navigation

    private void OnNavigationIndexChanged(int index)
    {
        SelectedMenuIndex = index;

        static bool IsChatTab(int i) => i is 1 or 2 or 5;
        if (IsChatTab(SelectedMenuIndex) && !IsChatTab(index))
        {
            ResetChat(_chatsVm);
            ResetChat(_contactsVm);
        }

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

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();

        MemoryDiagnostics.Dump($"NavigateTo index={index}");
    }

    [RelayCommand]
    private void SetItem(int index) => _navigator.NavigateTo(index, addToHistory: true);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigator.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _navigator.GoForward();

    public void SetActiveMenu(int index) => _navigator.NavigateTo(index, addToHistory: true);

    private bool HasAccessToMenu(int index) => index switch
    {
        4 => IsAdminSectionVisible,
        6 => IsDepartmentSectionVisible,
        _ => true
    };

    private void RefreshRoleBasedUi()
    {
        IsAdminSectionVisible = _auth.Session.IsAdmin;
        IsDepartmentSectionVisible = _auth.Session.IsHead;

        if ((SelectedMenuIndex == 4 && !IsAdminSectionVisible) ||
            (SelectedMenuIndex == 6 && !IsDepartmentSectionVisible))
            _navigator.NavigateTo(1, addToHistory: false);
    }

    private static void ResetChat(ChatListViewModel? vm)
    {
        if (vm == null) return;
        vm.CurrentChatViewModel = null;
        vm.SelectedChat = null;
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
        SearchManager.SearchQuery = value;
        OnPropertyChanged(nameof(IsSearchMode));
    }

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
    private async Task OpenSearchFilters()
    {
        SearchManager.EnterSearchMode();
        var dialog = new SearchFiltersDialogViewModel(SearchManager, () => SearchManager.ApplyFiltersAsync(), () => SearchManager.ApplyFiltersAsync());
        await ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenSearchedChat(ChatListItemViewModel? chat)
    {
        if (chat == null) return;
        await SwitchToTabAndOpenChatAsync(chat.ToDto());
        ClearSearch();
    }

    [RelayCommand]
    private async Task OpenSearchResult(GlobalSearchMessageDto? message)
    {
        if (message == null) return;
        await SwitchToTabAndOpenMessageAsync(message);
        ClearSearch();
    }

    [RelayCommand]
    private async Task LoadMoreSearchResults() =>
        await SearchManager.LoadMoreMessagesAsync();

    #endregion

    #region Chat open

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
        _navigator.NavigateTo(isGroup ? 1 : 5, addToHistory: true);
        await Task.Delay(50);

        var vm = isGroup ? (_chatsVm ??= _chatsFactory.Create(this, true)) : (_contactsVm ??= _chatsFactory.Create(this, false));

        CurrentMenuViewModel = vm;
        await vm.OpenChatByIdAsync(msg.ChatId, msg.Id);
    }

    public async Task NavigateToForwardedChatAsync(ChatDto targetChat)
    {
        bool isGroup = targetChat.Type is not ChatType.Contact;
        SetActiveMenu(isGroup ? 1 : 5);
        await Task.Delay(50);

        var vm = isGroup ? (_chatsVm ??= _chatsFactory.Create(this, true)) : (_contactsVm ??= _chatsFactory.Create(this, false));

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

    [RelayCommand]
    public async Task OpenOrCreateChatAsync(UserDto user)
    {
        SetActiveMenu(5);
        await Task.Delay(50);
        if (_contactsVm != null)
            await _contactsVm.OpenOrCreateDialogWithUserAsync(user);
    }

    private async Task OpenChatAsync(ChatDto chat)
    {
        _chatsVm ??= _chatsFactory.Create(this, true);
        await EnsureAndSelectChatAsync(_chatsVm, chat, 1);
    }

    private async Task EnsureAndSelectChatAsync(ChatListViewModel vm, ChatDto chat, int menuIndex)
    {
        _navigator.NavigateTo(menuIndex, addToHistory: true);
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
            chat = r is { Success: true, Data: not null } ? r.Data : throw new InvalidOperationException(r.Error ?? "Не удалось загрузить чат.");
            if (UserChats.All(c => c.Id != chat.Id))
                UserChats.Insert(0, chat);
        }

        if (notification.MessageId is { } msgId)
        {
            await SwitchToTabAndOpenMessageAsync(new GlobalSearchMessageDto
            {
                Id = msgId,
                ChatId = notification.ChatId,
                ChatType = chat.Type
            });
            return;
        }

        await SwitchToTabAndOpenChatAsync(chat);
    }

    #endregion

    #region Dialogs

    public Task ShowDialogAsync(DialogBaseViewModel vm)
        => _mainWindowVm.ShowDialogAsync(vm);

    public async Task ShowUserProfileAsync(int userId)
        => await SafeExecuteAsync(async () =>
        {
            var r = await _api.GetAsync<UserDto>(ApiEndpoints.Users.ById(userId));
            if (!r.Success || r.Data == null)
            {
                ErrorMessage = $"Не удалось загрузить профиль: {r.Error}";
                return;
            }
            var dlg = new UserProfileDialogViewModel(r.Data, _api)
            {
                CanSendMessage = r.Data.Id != _auth.Session.UserId,
                OpenChatWithUserAction = async u => await OpenOrCreateChatAsync(u)
            };
            await _mainWindowVm.ShowDialogAsync(dlg);
        });

    public async Task ShowPollDialogAsync(int chatId, Func<Task>? onCreated = null)
        => await _mainWindowVm.ShowDialogAsync(new PollDialogViewModel(chatId)
        {
            CreateAction = async dto =>
            {
                var r = await _api.PostAsync<CreatePollDto, MessageDto>(ApiEndpoints.Polls.Create, dto);
                if (r.Success) SuccessMessage = "Опрос создан";
                else ErrorMessage = $"Ошибка: {r.Error}";
                if (onCreated != null) await onCreated();
            }
        });

    public async Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onUpdated = null)
    {
        var members = (await _api.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(chat.Id))).Data;

        await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, _sp.GetRequiredService<IPlatformService>(), UserId, chat, members, _auth.Session.IsAdmin)
        {
            SaveAction = async (dto, mIds, aIds, s, n, rem) =>
            {
                var (ok, updated, error) = await _chatOps.UpdateGroupAsync(dto, mIds, aIds, s, n, rem);
                if (!ok) { ErrorMessage = error; return false; }
                var existing = UserChats.FirstOrDefault(c => c.Id == updated!.Id);
                if (existing != null) UserChats[UserChats.IndexOf(existing)] = updated!;
                onUpdated?.Invoke(updated!);
                SuccessMessage = "Группа обновлена";
                return true;
            },
            DeleteAction = DeleteGroupChatAsync,
            ShowDialogAction = vm => _mainWindowVm.ShowDialogAsync(vm)
        });
    }

    public async Task ShowCreateGroupDialogAsync(Action<ChatDto>? onCreated = null)
        => await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, _sp.GetRequiredService<IPlatformService>(), UserId, isSystemAdmin: _auth.Session.IsAdmin)
        {
            SaveAction = async (dto, mIds, aIds, s, n, _) =>
            {
                var (ok, chat, error) = await _chatOps.CreateGroupAsync(dto, mIds, aIds, s, n);
                if (!ok) { ErrorMessage = error; return false; }
                UserChats.Add(chat!);
                await OpenChatAsync(chat!);
                onCreated?.Invoke(chat!);
                SuccessMessage = "Группа создана";
                return true;
            },
            ShowDialogAction = vm => _mainWindowVm.ShowDialogAsync(vm)
        });

    private async Task<bool> DeleteGroupChatAsync(int chatId)
    {
        var (ok, error) = await _chatOps.DeleteAsync(chatId);
        if (!ok) { ErrorMessage = error; return false; }
        SuccessMessage = "Группа удалена";
        return true;
    }

    #endregion

    #region Hub events

    private void OnSessionChanged()
    {
        RefreshRoleBasedUi();
        if (_auth.Session.IsAuthenticated)
            _ = _hubManager.ReconnectAllAsync();
    }

    private async void OnUserRoleUpdated(UserRole role)
    {
        await _auth.UpdateRoleAsync(role);
        try { await _auth.TryRefreshTokenAsync(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenu] Token refresh failed: {ex.Message}");
        }

        var prevIndex = SelectedMenuIndex;
        RefreshRoleBasedUi();

        if (_adminVm != null)
            await _adminVm.RefreshCommand.ExecuteAsync(null);

        if ((prevIndex == 4 || prevIndex == 6) && !IsAdminSectionVisible && !IsDepartmentSectionVisible)
        {
            _navigator.NavigateTo(1, addToHistory: false);
            var notify = _sp.GetRequiredService<INotificationService>();
            await notify.ShowAsync("Права изменены", "Ваши права администратора отозваны.", DesktopNotificationType.Warning);
        }
    }

    private void OnUserPermissionsChanged(UserPermissionsChangedDto dto)
        => _sp.GetRequiredService<INotificationService>().Show("Права изменены", dto.Reason, DesktopNotificationType.Warning);

    private void OnUserBanned(UserBannedDto dto) => Dispatcher.UIThread.Post(async () =>
    {
        _sp.GetRequiredService<INotificationService>().Show("Доступ заблокирован", dto.Reason, DesktopNotificationType.Error);

        await Task.Delay(500);
        await _auth.LogoutAsync();
        _mainWindowVm.CurrentViewModel = _sp.GetRequiredService<LoginViewModel>();
    });

    #endregion

    #region Call helpers

    public void ShowCallView(CallStateDto state, string chatName, bool isGroupCall)
        => Dispatcher.UIThread.Post(() => ShowCallViewSync(state, chatName, isGroupCall, _sp.GetRequiredService<ActiveCallStore>(), _sp));
    [RelayCommand]
    private Task SetStatusAsync(string param) => Status.SetStatusAsync(param);

    public void OpenCallUi() => _sp.GetRequiredService<ActiveCallStore>().OpenCallUi();

    private static void ShowCallViewSync(CallStateDto state, string chatName, bool isGroupCall, ActiveCallStore store, IServiceProvider sp)
    {
        if (store.ActiveCall != null)
        {
            store.OpenCallUi();
            return;
        }

        var callVm = new CallViewModel(sp.GetRequiredService<ICallService>(), sp.GetRequiredService<ICallHubConnection>(),
            store, sp.GetRequiredService<ICallAudioService>());

        callVm.Initialize(state, chatName, isGroupCall, sp.GetRequiredService<IAuthManager>().Session.UserId ?? 0);

        store.ActiveCall = callVm;
        store.OpenCallUi();
    }

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigator.IndexChanged -= OnNavigationIndexChanged;
            _globalHub.UserRoleUpdated -= OnUserRoleUpdated;
            _globalHub.UserPermissionsChanged -= OnUserPermissionsChanged;
            _globalHub.UserBanned -= OnUserBanned;
            _auth.Session.SessionChanged -= OnSessionChanged;

            _callHandler.Dispose();
            Status.Dispose();
            CallBanner.Dispose();

            _ = _hubManager.DisposeAsync().AsTask();

            DisposeVm(ref _chatsVm);
            DisposeVm(ref _contactsVm);
            DisposeVm(ref _deptVm);
            DisposeVm(ref _profileVm);
            DisposeVm(ref _adminVm);
            DisposeVm(ref _settingsVm);
        }
        base.Dispose(disposing);
    }

    private static void DisposeVm<T>(ref T? vm) where T : BaseViewModel
    {
        vm?.Dispose();
        vm = null;
    }

    #endregion
}