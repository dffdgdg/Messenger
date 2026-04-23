using MessengerDesktop.Services.Call;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.ViewModels.Call;
using MessengerDesktop.ViewModels.Chat;
using MessengerDesktop.ViewModels.Chats;
using MessengerDesktop.ViewModels.Department;
using MessengerDesktop.ViewModels.Dialog;
using MessengerDesktop.ViewModels.Factories;
using MessengerShared.DTO.Call;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

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
    public CallBannerViewModel CallBanner { get; }

    [ObservableProperty] public partial BaseViewModel? CurrentMenuViewModel { get; set; }
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ObservableCollection<UserDto> AllContacts { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<ChatDto> UserChats { get; set; } = [];
    [ObservableProperty] public partial bool IsSearching { get; set; }
    [ObservableProperty] public partial int SelectedMenuIndex { get; set; } = 1;

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

        CurrentMenuViewModel = _chatsVm = _chatsFactory.Create(this, isGroupMode: true);

        _ = LoadContactsAndChatsAsync();

        if (_auth.Session.IsAuthenticated)
        {
            _ = InitGlobalHubAsync();
        }
    }
    private void OnSessionChanged()
    {
        if (_auth.Session.IsAuthenticated && !_callHub.IsConnected)
        {
            _ = InitGlobalHubAsync();
        }
    }

    private async Task InitGlobalHubAsync()
    {
        try
        {
            await _globalHub.ConnectAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to connect global hub: {ex.Message}");
        }

        try
        {
            await _callHub.ConnectAsync();
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
            if (!_callHub.IsConnected)
            {
                Debug.WriteLine("[MainMenuViewModel] IncomingCall: CallHub не подключён, пропуск");
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
            await callService.JoinCallAsync(invite.CallId, invite.ChatId);

            await Task.Delay(300);

            var state = await _callHub.GetCallStateAsync(invite.ChatId);
            if (state == null)
            {
                Debug.WriteLine("[MainMenuViewModel] OnCallAccepted: GetCallState вернул null");
                return;
            }

            var chatName = UserChats.FirstOrDefault(c => c.Id == invite.ChatId)?.Name
                           ?? invite.ChatName;

            _ = OpenCallChatAsync(invite);
            Dispatcher.UIThread.Post(() => _ = ShowCallViewAsync(state, chatName, invite.IsGroupCall));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenuViewModel] OnCallAccepted error: {ex.Message}");
        }
    }

    private Task ShowCallViewAsync(CallStateDto state, string chatName, bool isGroupCall)
    {
        if (_activeCallStore.ActiveCall != null)
        {
            _activeCallStore.OpenCallUi();
            return Task.CompletedTask;
        }

        var callService = _sp.GetRequiredService<ICallService>();
        var audioService = _sp.GetRequiredService<CallAudioService>();
        var callVm = new CallViewModel(callService, _callHub, _activeCallStore, audioService);
        callVm.Initialize(state, chatName, isGroupCall, _auth.Session.UserId ?? 0);

        _activeCallStore.ActiveCall = callVm;
        _activeCallStore.OpenCallUi();

        return Task.CompletedTask;
    }

    private void OnCallStateUpdated(CallStateDto state)
    {
        if (_activeCallStore.IsInCall) return;

        var callService = _sp.GetRequiredService<ICallService>();
        var myUserId = _auth.Session.UserId ?? 0;

        // Только инициатор — принимающая сторона обрабатывает в OnCallAcceptedAsync
        if (state.InitiatorId != myUserId) return;
        if (callService.ActiveChatId != state.ChatId) return;

        var chatName = UserChats.FirstOrDefault(c => c.Id == state.ChatId)?.Name ?? string.Empty;
        Dispatcher.UIThread.Post(() => _ = ShowCallViewAsync(state, chatName, state.IsGroupCall));
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

    private void NavigateTo(int index, bool addToHistory)
    {
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
    }

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

    public async Task SwitchToTabAndOpenChatAsync(ChatDto chat)
    {
        if (chat.Type is ChatType.Chat or ChatType.Department) { await OpenChatAsync(chat); return; }
        _contactsVm ??= _chatsFactory.Create(this, false);
        await EnsureAndSelectChatAsync(_contactsVm, chat, 5);
    }

    public async Task SwitchToTabAndOpenMessageAsync(GlobalSearchMessageDto msg)
    {
        bool isGroup = msg.ChatType is ChatType.Chat or ChatType.Department;
        SetActiveMenu(isGroup ? 1 : 5);
        await Task.Delay(50);

        var vm = isGroup ? (_chatsVm ??= _chatsFactory.Create(this, true)) : (_contactsVm ??= _chatsFactory.Create(this, false));

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
        if (!vm.Chats.Any(c => c.Id == chat.Id))
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
            await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, UserId, chat, members)
            {
                SaveAction = async (dto, mIds, aIds, s, n, rem) => await UpdateGroupChatAsync(dto, mIds, aIds, s, n, rem, onUpdated),
                ShowDialogAction = vm => _mainWindowVm.ShowDialogAsync(vm)
            });
        }
        catch (Exception ex) { ErrorMessage = $"Ошибка открытия диалога: {ex.Message}"; }
    }

    public async Task ShowCreateGroupDialogAsync(Action<ChatDto>? onCreated = null)
    {
        try
        {
            await _mainWindowVm.ShowDialogAsync(new ChatEditDialogViewModel(_api, UserId)
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

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _api.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        var chatsTask = _api.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(UserId));
        await Task.WhenAll(usersTask, chatsTask);

        if (await usersTask is { Success: true, Data: { } users })
            AllContacts = new ObservableCollection<UserDto>(users.Where(u => u.Id != UserId));
        if (await chatsTask is { Success: true, Data: { } chats })
            UserChats = new ObservableCollection<ChatDto>(chats);
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

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _auth.Session.SessionChanged -= OnSessionChanged;
            _callHub.IncomingCall -= OnIncomingCall;
            _callHub.CallStateUpdated -= OnCallStateUpdated;
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