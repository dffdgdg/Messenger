using MessengerDesktop.Services.Realtime;
using MessengerDesktop.ViewModels.Chat;
using MessengerDesktop.ViewModels.Chats;
using MessengerDesktop.ViewModels.Department;
using MessengerDesktop.ViewModels.Dialog;
using MessengerDesktop.ViewModels.Factories;
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
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatsViewModelFactory _chatsViewModelFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IGlobalHubConnection _globalHub;
    private readonly Stack<int> _backHistory = [];
    private readonly Stack<int> _forwardHistory = [];

    private ChatsViewModel? _chatsViewModel;
    private ChatsViewModel? _contactsViewModel;
    private DepartmentManagementViewModel? _departmentViewModel;
    private ProfileViewModel? _profileViewModel;
    private AdminViewModel? _adminViewModel;
    private SettingsViewModel? _settingsViewModel;
    private StyleGuideViewModel? _styleGuideViewModel;
    private CancellationTokenSource? _searchCts;

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

    public MainMenuViewModel(MainWindowViewModel mainWindowViewModel, IApiClientService apiClient, IAuthManager authManager,
        IChatsViewModelFactory chatsViewModelFactory, IServiceProvider serviceProvider, IGlobalHubConnection globalHub)
    {
        _globalHub = globalHub;
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _chatsViewModelFactory = chatsViewModelFactory ?? throw new ArgumentNullException(nameof(chatsViewModelFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        UserId = _authManager.Session.UserId ?? throw new InvalidOperationException("User not authenticated");

        _chatsViewModel = _chatsViewModelFactory.Create(this, isGroupMode: true);
        CurrentMenuViewModel = _chatsViewModel;

        _ = LoadContactsAndChatsAsync();
        _ = InitializeGlobalHubAsync();
    }

    private async Task InitializeGlobalHubAsync()
    {
        try
        {
            await _globalHub.ConnectAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to connect global hub: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SetItem(int index) => NavigateToMenu(index, addToHistory: true);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!CanGoBack) return;

        var previousIndex = _backHistory.Pop();

        if (SelectedMenuIndex != previousIndex)
            _forwardHistory.Push(SelectedMenuIndex);

        NavigateToMenu(previousIndex, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (!CanGoForward) return;

        var nextIndex = _forwardHistory.Pop();

        if (SelectedMenuIndex != nextIndex)
            _backHistory.Push(SelectedMenuIndex);

        NavigateToMenu(nextIndex, addToHistory: false);
    }

    public void SetActiveMenu(int index) => NavigateToMenu(index, addToHistory: true);

    private void NavigateToMenu(int index, bool addToHistory)
    {
        if (addToHistory && SelectedMenuIndex != index)
        {
            _backHistory.Push(SelectedMenuIndex);
            _forwardHistory.Clear();
        }

        SelectedMenuIndex = index;
        ClearSearch();

        CurrentMenuViewModel = index switch
        {
            0 => _settingsViewModel ??= _serviceProvider.GetRequiredService<SettingsViewModel>(),
            1 or 2 => _chatsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: true),
            3 => _profileViewModel ??= _serviceProvider.GetRequiredService<ProfileViewModel>(),
            4 => _adminViewModel ??= _serviceProvider.GetRequiredService<AdminViewModel>(),
            5 => _contactsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: false),
            6 => _styleGuideViewModel ??= _serviceProvider.GetRequiredService<StyleGuideViewModel>(),
            7 => GetOrCreateDepartmentViewModel(),
            _ => CurrentMenuViewModel
        };

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private DepartmentManagementViewModel GetOrCreateDepartmentViewModel()
    {
        if (_departmentViewModel != null)
            return _departmentViewModel;

        _departmentViewModel = _serviceProvider.GetRequiredService<DepartmentManagementViewModel>();

        _departmentViewModel.OpenChatWithUserAction = async user => await OpenOrCreateChatAsync(user);

        _departmentViewModel.NavigateToChatAction = async chatId =>
        {
            var chat = UserChats.FirstOrDefault(c => c.Id == chatId);
            if (chat != null)
                await OpenChatAsync(chat);
        };

        _departmentViewModel.ShowRemoveConfirmAction = async member =>
        {
            var dialog = new ConfirmDialogViewModel("Удаление из отдела",
                $"Вы уверены, что хотите удалить {member.DisplayName} из отдела?",
                "Удалить", "Отмена");
            await _mainWindowViewModel.ShowDialogAsync(dialog);
            return await dialog.Result;
        };

        _departmentViewModel.ShowSelectUserAction = async users =>
        {
            var pickerDialog = new UserPickerDialogViewModel("Добавить сотрудника", users);
            await _mainWindowViewModel.ShowDialogAsync(pickerDialog);
            return await pickerDialog.SingleSelectResult;
        };

        return _departmentViewModel;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowNoResults));

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    public async Task SwitchToTabAndOpenChatAsync(ChatDto chat)
    {
        if (chat.Type is ChatType.Chat or ChatType.Department)
        {
            await OpenChatAsync(chat);
            return;
        }

        SetActiveMenu(5);
        await Task.Delay(50);

        _contactsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: false);

        if (!_contactsViewModel.Chats.Any(c => c.Id == chat.Id))
            _contactsViewModel.Chats.Insert(0, new ChatListItemViewModel(chat));

        _contactsViewModel.SelectedChat = _contactsViewModel.Chats.FirstOrDefault(c => c.Id == chat.Id);
        CurrentMenuViewModel = _contactsViewModel;
    }

    public async Task SwitchToTabAndOpenMessageAsync(GlobalSearchMessageDto message)
    {
        bool isGroupChat = message.ChatType is ChatType.Chat or ChatType.Department;
        SetActiveMenu(isGroupChat ? 1 : 5);
        await Task.Delay(50);

        var targetViewModel = isGroupChat ? _chatsViewModel : _contactsViewModel;

        if (targetViewModel == null)
        {
            if (isGroupChat)
            {
                _chatsViewModel = _chatsViewModelFactory.Create(this, isGroupMode: true);
                targetViewModel = _chatsViewModel;
            }
            else
            {
                _contactsViewModel = _chatsViewModelFactory.Create(this, isGroupMode: false);
                targetViewModel = _contactsViewModel;
            }
            CurrentMenuViewModel = targetViewModel;
        }

        await targetViewModel.OpenChatByIdAsync(message.ChatId, message.Id);
    }

    public async Task OpenNotificationAsync(NotificationDto notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var chat = UserChats.FirstOrDefault(c => c.Id == notification.ChatId);

        if (chat == null)
        {
            var result = await _apiClient.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(notification.ChatId));
            if (!result.Success || result.Data == null)
                throw new InvalidOperationException(result.Error ?? "Не удалось загрузить чат из уведомления.");

            chat = result.Data;

            if (UserChats.All(c => c.Id != chat.Id))
                UserChats.Insert(0, chat);
        }

        if (notification.MessageId.HasValue)
        {
            await SwitchToTabAndOpenMessageAsync(new GlobalSearchMessageDto
            {
                Id = notification.MessageId.Value,
                ChatId = notification.ChatId,
                ChatType = chat.Type
            });
            return;
        }

        await SwitchToTabAndOpenChatAsync(chat);
    }

    [RelayCommand]
    public async Task OpenOrCreateChatAsync(UserDto user)
    {
        SetActiveMenu(5);
        await Task.Delay(50);

        if (_contactsViewModel != null)
            await _contactsViewModel.OpenOrCreateDialogWithUserAsync(user);
    }

    private async Task OpenChatAsync(ChatDto chat)
    {
        SetActiveMenu(1);
        await Task.Delay(50);

        _chatsViewModel ??= _chatsViewModelFactory.Create(this, true);

        if (!_chatsViewModel.Chats.Any(c => c.Id == chat.Id))
            _chatsViewModel.Chats.Add(new ChatListItemViewModel(chat));

        _chatsViewModel.SelectedChat = _chatsViewModel.Chats.FirstOrDefault(c => c.Id == chat.Id);
        CurrentMenuViewModel = _chatsViewModel;
    }

    public async Task ShowUserProfileAsync(int userId) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.GetAsync<UserDto>(ApiEndpoints.Users.ById(userId));
        if (!result.Success || result.Data == null)
        {
            ErrorMessage = $"Не удалось загрузить профиль: {result.Error}";
            return;
        }

        var dialog = new UserProfileDialogViewModel(result.Data, _apiClient)
        {
            CanSendMessage = result.Data.Id != _authManager.Session.UserId,
            OpenChatWithUserAction = async user => await OpenOrCreateChatAsync(user)
        };

        await _mainWindowViewModel.ShowDialogAsync(dialog);
    });

    public async Task ShowPollDialogAsync(int chatId, Func<Task>? onCreated = null)
    {
        try
        {
            var pollDialog = new PollDialogViewModel(chatId)
            {
                CreateAction = async createPollDto =>
                {
                    await CreatePollAsync(createPollDto);
                    if (onCreated != null)
                        await onCreated();
                }
            };

            await _mainWindowViewModel.ShowDialogAsync(pollDialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    public async Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onUpdated = null)
    {
        try
        {
            var membersResult = await _apiClient.GetAsync<List<ChatMemberDto>>(
                ApiEndpoints.Chats.MembersDetailed(chat.Id));
            var members = membersResult.Success ? membersResult.Data : null;

            var dialog = new ChatEditDialogViewModel(_apiClient, UserId, chat, members)
            {
                SaveAction = async (chatDto, memberIds, adminIds, avatarStream, avatarFileName, isAvatarRemoved)
                    => await UpdateGroupChatAsync(chatDto, memberIds, adminIds, avatarStream, avatarFileName, isAvatarRemoved, onUpdated),
                ShowDialogAction = dialogVm => _mainWindowViewModel.ShowDialogAsync(dialogVm)
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    public async Task ShowCreateGroupDialogAsync(Action<ChatDto>? onGroupCreated = null)
    {
        try
        {
            var dialog = new ChatEditDialogViewModel(_apiClient, UserId)
            {
                SaveAction = async (chatDto, memberIds, adminIds, avatarStream, avatarFileName, _)
                    => await CreateGroupChatAsync(chatDto, memberIds, adminIds, avatarStream, avatarFileName, onGroupCreated),
                ShowDialogAction = dialogVm => _mainWindowViewModel.ShowDialogAsync(dialogVm)
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    private async Task CreatePollAsync(CreatePollDto dto) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.PostAsync<CreatePollDto, MessageDto>(ApiEndpoints.Polls.Create, dto);

        if (result.Success)
            SuccessMessage = "Опрос создан";
        else
            ErrorMessage = $"Ошибка создания опроса: {result.Error}";
    });

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        var chatsTask = _apiClient.GetAsync<List<ChatDto>>(ApiEndpoints.Chats.UserChats(UserId));
        await Task.WhenAll(usersTask, chatsTask);

        var usersResult = await usersTask;
        var chatsResult = await chatsTask;

        if (usersResult.Success && usersResult.Data != null)
            AllContacts = new ObservableCollection<UserDto>(usersResult.Data.Where(u => u.Id != UserId));

        if (chatsResult.Success && chatsResult.Data != null)
            UserChats = new ObservableCollection<ChatDto>(chatsResult.Data);
    });

    private async Task<bool> CreateGroupChatAsync(ChatDto chatDto, List<int> memberIds, List<int> adminIds,
        Stream? avatarStream, string? avatarFileName, Action<ChatDto>? onSuccess)
    {
        try
        {
            var createResult = await _apiClient.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chats.Create, chatDto);

            if (!createResult.Success || createResult.Data == null)
            {
                ErrorMessage = $"Ошибка создания группы: {createResult.Error}";
                return false;
            }

            var createdChat = createResult.Data;

            foreach (var userId in memberIds)
                await _apiClient.PostAsync(ApiEndpoints.Chats.Members(createdChat.Id), new UpdateChatMemberDto { UserId = userId });

            foreach (var adminId in adminIds)
                await _apiClient.PutAsync(ApiEndpoints.Chats.MemberRole(createdChat.Id, adminId, ChatRole.Admin), null!);

            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                avatarStream.Position = 0;
                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDto>(
                    ApiEndpoints.Chats.Avatar(createdChat.Id), avatarStream, avatarFileName, GetMimeType(avatarFileName));

                if (avatarResult.Success && avatarResult.Data != null)
                    createdChat.Avatar = avatarResult.Data.AvatarUrl;
            }

            UserChats.Add(createdChat);
            await OpenChatAsync(createdChat);

            onSuccess?.Invoke(createdChat);
            SuccessMessage = "Группа успешно создана";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> UpdateGroupChatAsync(ChatDto chatDto, List<int> memberIds, List<int> adminIds,
        Stream? avatarStream, string? avatarFileName, bool isAvatarRemoved, Action<ChatDto>? onSuccess)
    {
        try
        {
            var updateDto = new UpdateChatDto { Id = chatDto.Id, Name = chatDto.Name, ChatType = ChatType.Chat };

            var updateResult = await _apiClient.PutAsync<UpdateChatDto, ChatDto>(
                ApiEndpoints.Chats.ById(chatDto.Id), updateDto);

            if (!updateResult.Success || updateResult.Data == null)
            {
                ErrorMessage = $"Ошибка обновления группы: {updateResult.Error}";
                return false;
            }

            var updatedChat = updateResult.Data;

            await SyncChatMembersAsync(chatDto.Id, memberIds, adminIds, chatDto.CreatedById);

            var (avatarOk, newAvatarUrl, avatarError) = await UpdateChatAvatarAsync(
                chatDto.Id, avatarStream, avatarFileName, isAvatarRemoved);

            if (!avatarOk)
            {
                ErrorMessage = avatarError;
                return false;
            }

            if (newAvatarUrl != null)
                updatedChat.Avatar = newAvatarUrl;

            var existingChat = UserChats.FirstOrDefault(c => c.Id == chatDto.Id);
            if (existingChat != null)
                UserChats[UserChats.IndexOf(existingChat)] = updatedChat;

            onSuccess?.Invoke(updatedChat);
            SuccessMessage = "Группа успешно обновлена";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
            return false;
        }
    }

    private async Task SyncChatMembersAsync(int chatId, List<int> memberIds, List<int> adminIds, int createdById)
    {
        var currentMembersResult = await _apiClient.GetAsync<List<ChatMemberDto>>(
            ApiEndpoints.Chats.MembersDetailed(chatId));
        var currentMembers = currentMembersResult.Data ?? [];
        var currentMemberIds = currentMembers.Select(m => m.UserId).ToHashSet();
        var currentAdminIds = currentMembers
            .Where(x => x.Role is ChatRole.Admin or ChatRole.Owner)
            .Select(x => x.UserId).ToHashSet();

        foreach (var userId in memberIds.Where(id => !currentMemberIds.Contains(id)))
            await _apiClient.PostAsync(ApiEndpoints.Chats.Members(chatId), new UpdateChatMemberDto { UserId = userId });

        foreach (var userId in currentMemberIds.Where(id => !memberIds.Contains(id) && id != UserId))
            await _apiClient.DeleteAsync(ApiEndpoints.Chats.RemoveMember(chatId, userId));

        foreach (var adminId in adminIds.Where(id => currentMemberIds.Contains(id) && !currentAdminIds.Contains(id)))
            await _apiClient.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, adminId, ChatRole.Admin), null!);

        foreach (var memberId in currentAdminIds.Where(id => id != createdById && !adminIds.Contains(id) && currentMemberIds.Contains(id)))
            await _apiClient.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, memberId, ChatRole.Member), null!);
    }

    private async Task<(bool Success, string? AvatarUrl, string? Error)> UpdateChatAvatarAsync(
        int chatId, Stream? avatarStream, string? avatarFileName, bool isAvatarRemoved)
    {
        if (avatarStream == null || string.IsNullOrEmpty(avatarFileName))
            return (true, null, null);

        avatarStream.Position = 0;
        var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDto>(
            ApiEndpoints.Chats.Avatar(chatId), avatarStream, avatarFileName, GetMimeType(avatarFileName));

        if (avatarResult.Success && avatarResult.Data != null)
            return (true, avatarResult.Data.AvatarUrl, null);

        if (!isAvatarRemoved)
            return (true, null, null);

        var deleteResult = await _apiClient.DeleteAsync(ApiEndpoints.Chats.Avatar(chatId));
        return deleteResult.Success
            ? (true, string.Empty, null)
            : (false, null, $"Ошибка удаления аватара: {deleteResult.Error}");
    }

    private static string GetMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;

            if (_globalHub is IAsyncDisposable asyncDisposable)
                _ = DisposeGlobalHubAsync(asyncDisposable);
        }

        base.Dispose(disposing);
    }

    private static async Task DisposeGlobalHubAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainMenuViewModel] Global hub dispose error: {ex.Message}");
        }
    }
}