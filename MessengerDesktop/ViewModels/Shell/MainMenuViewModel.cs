using MessengerDesktop.Services.Realtime;
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

public partial class MainMenuViewModel : BaseViewModel
{
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly IChatsViewModelFactory _chatsViewModelFactory;
    private readonly IServiceProvider _serviceProvider;

    private ChatsViewModel? _chatsViewModel;
    private ChatsViewModel? _contactsViewModel;
    private DepartmentManagementViewModel? _departmentViewModel;
    private ProfileViewModel? _profileViewModel;
    private AdminViewModel? _adminViewModel;
    private SettingsViewModel? _settingsViewModel;

    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private BaseViewModel? _currentMenuViewModel;

    [ObservableProperty]
    private int _userId;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<UserDto> _allContacts = [];

    [ObservableProperty]
    private ObservableCollection<ChatDto> _userChats = [];

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _selectedMenuIndex = 1;
    private readonly IGlobalHubConnection _globalHub;
    private readonly Stack<int> _backHistory = [];
    private readonly Stack<int> _forwardHistory = [];

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchText  && !IsSearching;
    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;

    public MainMenuViewModel(MainWindowViewModel mainWindowViewModel,IApiClientService apiClient,IAuthManager authManager,
        IChatsViewModelFactory chatsViewModelFactory,IServiceProvider serviceProvider, IGlobalHubConnection globalHub)
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
    private void SetItem(int index)
        => NavigateToMenu(index, addToHistory: true);


    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!CanGoBack)
            return;

        var previousIndex = _backHistory.Pop();

        if (SelectedMenuIndex != previousIndex)
        {
            _forwardHistory.Push(SelectedMenuIndex);
        }

        NavigateToMenu(previousIndex, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (!CanGoForward)
            return;

        var nextIndex = _forwardHistory.Pop();

        if (SelectedMenuIndex != nextIndex)
        {
            _backHistory.Push(SelectedMenuIndex);
        }

        NavigateToMenu(nextIndex, addToHistory: false);
    }

    private void NavigateToMenu(int index, bool addToHistory)
    {
        if (addToHistory && SelectedMenuIndex != index)
        {
            _backHistory.Push(SelectedMenuIndex);
            _forwardHistory.Clear();
        }
        SelectedMenuIndex = index;
        ClearSearch();

        switch (index)
        {
            case 0:
                _settingsViewModel ??= _serviceProvider.GetRequiredService<SettingsViewModel>();
                CurrentMenuViewModel = _settingsViewModel;
                break;
            case 1:
            case 2:
                _chatsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: true);
                CurrentMenuViewModel = _chatsViewModel;
                break;
            case 3:
                _profileViewModel ??= _serviceProvider.GetRequiredService<ProfileViewModel>();
                CurrentMenuViewModel = _profileViewModel;
                break;
            case 4:
                _adminViewModel ??= _serviceProvider.GetRequiredService<AdminViewModel>();
                CurrentMenuViewModel = _adminViewModel;
                break;
            case 5:
                _contactsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: false);
                CurrentMenuViewModel = _contactsViewModel;
                break;
            case 6:
                if (_departmentViewModel == null)
                {
                    _departmentViewModel = _serviceProvider.GetRequiredService<DepartmentManagementViewModel>();

                    _departmentViewModel.OpenChatWithUserAction = async user => await OpenOrCreateChatAsync(user);

                    _departmentViewModel.NavigateToChatAction = async chatId =>
                    {
                        var chat = UserChats.FirstOrDefault(c => c.Id == chatId);
                        if (chat != null)
                        {
                            await OpenChatAsync(chat);
                        }
                    };

                    _departmentViewModel.ShowRemoveConfirmAction = async member =>
                    {
                        var dialog = new ConfirmDialogViewModel("Удаление из отдела",
                            $"Вы уверены, что хотите удалить {member.DisplayName} из отдела?",
                            "Удалить","Отмена");
                        await _mainWindowViewModel.ShowDialogAsync(dialog);
                        return await dialog.Result;
                    };

                    _departmentViewModel.ShowSelectUserAction = async users =>
                    {
                        var dialog = new SelectUserDialogViewModel(users, "Добавить сотрудника");
                        await _mainWindowViewModel.ShowDialogAsync(dialog);
                        return await dialog.Result;
                    };
                }
                CurrentMenuViewModel = _departmentViewModel;
                break;
        }
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Переключиться на нужную вкладку и открыть чат (универсальный метод)
    /// </summary>
    public async Task SwitchToTabAndOpenChatAsync(ChatDto chat)
    {
        // Определяем нужную вкладку по типу чата
        bool isGroupChat = chat.Type == ChatType.Chat || chat.Type == ChatType.Department;

        if (isGroupChat)
        {
            // Используем существующий метод для групп
            await OpenChatAsync(chat);
        }
        else
        {
            // Для контактов переключаемся на вкладку контактов
            SetActiveMenu(5);
            await Task.Delay(50);

            _contactsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: false);

            if (!_contactsViewModel.Chats.Any(c => c.Id == chat.Id))
            {
                _contactsViewModel.Chats.Insert(0, new ChatListItemViewModel(chat));
            }

            _contactsViewModel.SelectedChat = _contactsViewModel.Chats.FirstOrDefault(c => c.Id == chat.Id);
            CurrentMenuViewModel = _contactsViewModel;
        }
    }
    /// <summary>
    /// Переключиться на нужную вкладку и открыть сообщение
    /// </summary>
    public async Task SwitchToTabAndOpenMessageAsync(GlobalSearchMessageDto message)
    {
        bool isGroupChat = message.ChatType == ChatType.Chat || message.ChatType == ChatType.Department;
        int targetIndex = isGroupChat ? 1 : 5;
        SetActiveMenu(targetIndex);
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
    [RelayCommand]
    public async Task OpenOrCreateChatAsync(UserDto user)
    {
        SetActiveMenu(5);

        await Task.Delay(50);

        if (_contactsViewModel != null)
        {
            await _contactsViewModel.OpenOrCreateDialogWithUserAsync(user);
        }
    }

    private async Task OpenChatAsync(ChatDto chat)
    {
        SetActiveMenu(1);

        await Task.Delay(50);

        _chatsViewModel ??= _chatsViewModelFactory.Create(this, true);

        if (!_chatsViewModel.Chats.Any(c => c.Id == chat.Id))
        {
            _chatsViewModel.Chats.Add(new ChatListItemViewModel(chat));
        }

        _chatsViewModel.SelectedChat = _chatsViewModel.Chats.FirstOrDefault(c => c.Id == chat.Id);
        CurrentMenuViewModel = _chatsViewModel;
        await Task.CompletedTask;
    }

    public async Task ShowUserProfileAsync(int userId) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.GetAsync<UserDto>(ApiEndpoints.User.ById(userId));
        if (result.Success && result.Data != null)
        {
            var currentUserId = _authManager.Session.UserId;

            var dialog = new UserProfileDialogViewModel(result.Data, _apiClient)
            {
                CanSendMessage = result.Data.Id != currentUserId,

                OpenChatWithUserAction = async user => await OpenOrCreateChatAsync(user)
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        else
        {
            ErrorMessage = $"Не удалось загрузить профиль: {result.Error}";
        }
    });

    public async Task ShowPollDialogAsync(int chatId, Action? onPollCreated = null)
    {
        try
        {
            var pollDialog = new PollDialogViewModel(chatId)
            {
                CreateAction = async createPollDto =>
                {
                    await CreatePollAsync(createPollDto);
                    onPollCreated?.Invoke();
                }
            };

            await _mainWindowViewModel.ShowDialogAsync(pollDialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    private async Task CreatePollAsync(CreatePollDto dto) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.PostAsync<CreatePollDto, MessageDto>(ApiEndpoints.Poll.Create, dto);

        if (result.Success)
            SuccessMessage = "Опрос создан";
        else
            ErrorMessage = $"Ошибка создания опроса: {result.Error}";
    });

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.User.GetAll);
        var chatsTask = _apiClient.GetAsync<List<ChatDto>>(ApiEndpoints.Chat.UserChats(UserId));
        await Task.WhenAll(usersTask, chatsTask);
        var usersResult = await usersTask;
        var chatsResult = await chatsTask;
        if (usersResult.Success && usersResult.Data != null)
        {
            AllContacts = new ObservableCollection<UserDto>(usersResult.Data.Where(u => u.Id != UserId));
        }
        if (chatsResult.Success && chatsResult.Data != null)
        {
            UserChats = new ObservableCollection<ChatDto>(chatsResult.Data);
        }
    });

    /// <summary>
    /// Показать диалог создания новой группы
    /// </summary>
    public async Task ShowCreateGroupDialogAsync(Action<ChatDto>? onGroupCreated = null)
    {
        try
        {
            var dialog = new ChatEditDialogViewModel(_apiClient, UserId)
            {
                SaveAction = async (chatDto, memberIds, avatarStream, avatarFileName)
                => await CreateGroupChatAsync(chatDto, memberIds, avatarStream, avatarFileName, onGroupCreated)
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;

            if (_globalHub is IAsyncDisposable asyncDisposable)
            {
                _ = DisposeGlobalHubAsync(asyncDisposable);
            }
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

    /// <summary>
    /// Показать диалог редактирования группы
    /// </summary>
    public async Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onGroupUpdated = null)
    {
        try
        {
            var membersResult = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Chat.Members(chat.Id));
            var members = membersResult.Success ? membersResult.Data : null;

            var dialog = new ChatEditDialogViewModel(_apiClient, UserId, chat, members)
            {
                SaveAction = async (chatDto, memberIds, avatarStream, avatarFileName)
                => await UpdateGroupChatAsync(chatDto, memberIds, avatarStream, avatarFileName, onGroupUpdated)
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    private async Task<bool> CreateGroupChatAsync(ChatDto chatDto,List<int> memberIds,Stream? avatarStream,string? avatarFileName,Action<ChatDto>? onSuccess)
    {
        try
        {
            var createResult = await _apiClient.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chat.Create, chatDto);

            if (!createResult.Success || createResult.Data == null)
            {
                ErrorMessage = $"Ошибка создания группы: {createResult.Error}";
                return false;
            }

            var createdChat = createResult.Data;

            foreach (var userId in memberIds)
            {
                await _apiClient.PostAsync(ApiEndpoints.Chat.Members(createdChat.Id), new UpdateChatMemberDto { UserId = userId });
            }

            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDto>(ApiEndpoints.Chat.Avatar(createdChat.Id),avatarStream,avatarFileName,contentType);

                if (avatarResult.Success && avatarResult.Data != null)
                {
                    createdChat.Avatar = avatarResult.Data.AvatarUrl;
                }
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


    private async Task<bool> UpdateGroupChatAsync(ChatDto chatDto, List<int> memberIds, Stream? avatarStream, string? avatarFileName, Action<ChatDto>? onSuccess)
    {
        try
        {
            var updateDto = new UpdateChatDto
            {
                Id = chatDto.Id,
                Name = chatDto.Name,
                ChatType = ChatType.Chat
            };

            var updateResult = await _apiClient.PutAsync<UpdateChatDto, ChatDto>(ApiEndpoints.Chat.ById(chatDto.Id), updateDto);

            if (!updateResult.Success || updateResult.Data == null)
            {
                ErrorMessage = $"Ошибка обновления группы: {updateResult.Error}";
                return false;
            }

            var updatedChat = updateResult.Data;

            var currentMembersResult = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Chat.Members(chatDto.Id));
            var currentMemberIds = currentMembersResult.Data?.Select(m => m.Id).ToHashSet() ?? [];

            foreach (var userId in memberIds.Where(id => !currentMemberIds.Contains(id)))
            {
                await _apiClient.PostAsync(ApiEndpoints.Chat.Members(chatDto.Id), new UpdateChatMemberDto { UserId = userId });
            }

            foreach (var userId in currentMemberIds.Where(id => !memberIds.Contains(id) && id != UserId))
            {
                await _apiClient.DeleteAsync(ApiEndpoints.Chat.RemoveMember(chatDto.Id, userId));
            }

            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDto>(ApiEndpoints.Chat.Avatar(chatDto.Id),avatarStream,
                    avatarFileName,contentType);

                if (avatarResult.Success && avatarResult.Data != null)
                {
                    updatedChat.Avatar = avatarResult.Data.AvatarUrl;
                }
            }

            var existingChat = UserChats.FirstOrDefault(c => c.Id == chatDto.Id);
            if (existingChat != null)
            {
                var index = UserChats.IndexOf(existingChat);
                UserChats[index] = updatedChat;
            }

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

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    public void SetActiveMenu(int index) =>
        NavigateToMenu(index, addToHistory: true);
}
