using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.ViewModels.Department;
using MessengerDesktop.ViewModels.Factories;
using MessengerShared.DTO;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.Chat.Poll;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private ObservableCollection<UserDTO> _allContacts = [];

    [ObservableProperty]
    private ObservableCollection<ChatDTO> _userChats = [];

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _selectedMenuIndex = 1;
    private readonly IGlobalHubConnection _globalHub;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchText  && !IsSearching;

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
    {
        SelectedMenuIndex = index;
        ClearSearch();

        switch (index)
        {
            case 0:
                _settingsViewModel ??= _serviceProvider.GetRequiredService<SettingsViewModel>();
                CurrentMenuViewModel = _settingsViewModel;
                break;
            case 1:
                _chatsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: true);
                CurrentMenuViewModel = _chatsViewModel;
                break;
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
                        var dialog = new ConfirmDialogViewModel(
                            "Удаление из отдела",
                            $"Вы уверены, что хотите удалить {member.DisplayName} из отдела?",
                            "Удалить",
                            "Отмена");
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
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowNoResults));

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>
    /// Переключиться на нужную вкладку и открыть чат (универсальный метод)
    /// </summary>
    public async Task SwitchToTabAndOpenChatAsync(ChatDTO chat)
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
                _contactsViewModel.Chats.Insert(0, chat);
            }

            _contactsViewModel.SelectedChat = chat;
            CurrentMenuViewModel = _contactsViewModel;
        }
    }
    /// <summary>
    /// Переключиться на нужную вкладку и открыть сообщение
    /// </summary>
    public async Task SwitchToTabAndOpenMessageAsync(GlobalSearchMessageDTO message)
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
    public async Task OpenOrCreateChatAsync(UserDTO user)
    {
        SetActiveMenu(5);

        await Task.Delay(50);

        if (_contactsViewModel != null)
        {
            await _contactsViewModel.OpenOrCreateDialogWithUserAsync(user);
        }
    }

    private async Task OpenChatAsync(ChatDTO chat)
    {
        SetActiveMenu(1);

        await Task.Delay(50);

        _chatsViewModel ??= _chatsViewModelFactory.Create(this, true);

        if (!_chatsViewModel.Chats.Any(c => c.Id == chat.Id))
        {
            _chatsViewModel.Chats.Add(chat);
        }

        _chatsViewModel.SelectedChat = chat;
        CurrentMenuViewModel = _chatsViewModel;
        await Task.CompletedTask;
    }

    public async Task ShowUserProfileAsync(int userId) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.GetAsync<UserDTO>(ApiEndpoints.User.ById(userId));
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

    private async Task CreatePollAsync(CreatePollDTO dto) => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.PostAsync<CreatePollDTO, MessageDTO>(ApiEndpoints.Poll.Create, dto);

        if (result.Success)
            SuccessMessage = "Опрос создан";
        else
            ErrorMessage = $"Ошибка создания опроса: {result.Error}";
    });

    private async Task LoadContactsAndChatsAsync() => await SafeExecuteAsync(async () =>
    {
        var usersTask = _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.User.GetAll);
        var chatsTask = _apiClient.GetAsync<List<ChatDTO>>(ApiEndpoints.Chat.UserChats(UserId));
        await Task.WhenAll(usersTask, chatsTask);
        var usersResult = await usersTask;
        var chatsResult = await chatsTask;
        if (usersResult.Success && usersResult.Data != null)
        {
            AllContacts = new ObservableCollection<UserDTO>(usersResult.Data.Where(u => u.Id != UserId));
        }
        if (chatsResult.Success && chatsResult.Data != null)
        {
            UserChats = new ObservableCollection<ChatDTO>(chatsResult.Data);
        }
    });

    /// <summary>
    /// Показать диалог создания новой группы
    /// </summary>
    public async Task ShowCreateGroupDialogAsync(Action<ChatDTO>? onGroupCreated = null)
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

    protected override async void Dispose(bool disposing)
    {
        if (disposing && _globalHub is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Показать диалог редактирования группы
    /// </summary>
    public async Task ShowEditGroupDialogAsync(ChatDTO chat, Action<ChatDTO>? onGroupUpdated = null)
    {
        try
        {
            var membersResult = await _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.Chat.Members(chat.Id));
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

    private async Task<bool> CreateGroupChatAsync(ChatDTO chatDto,List<int> memberIds,Stream? avatarStream,string? avatarFileName,Action<ChatDTO>? onSuccess)
    {
        try
        {
            var createResult = await _apiClient.PostAsync<ChatDTO, ChatDTO>(ApiEndpoints.Chat.Create, chatDto);

            if (!createResult.Success || createResult.Data == null)
            {
                ErrorMessage = $"Ошибка создания группы: {createResult.Error}";
                return false;
            }

            var createdChat = createResult.Data;

            foreach (var userId in memberIds)
            {
                await _apiClient.PostAsync(ApiEndpoints.Chat.Members(createdChat.Id), new { userId });
            }

            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDTO>(ApiEndpoints.Chat.Avatar(createdChat.Id),avatarStream,avatarFileName,contentType);

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


    private async Task<bool> UpdateGroupChatAsync(ChatDTO chatDto, List<int> memberIds, Stream? avatarStream, string? avatarFileName, Action<ChatDTO>? onSuccess)
    {
        try
        {
            var updateDto = new UpdateChatDTO
            {
                Id = chatDto.Id,
                Name = chatDto.Name,
                ChatType = ChatType.Chat
            };

            var updateResult = await _apiClient.PutAsync<UpdateChatDTO, ChatDTO>(ApiEndpoints.Chat.ById(chatDto.Id), updateDto);

            if (!updateResult.Success || updateResult.Data == null)
            {
                ErrorMessage = $"Ошибка обновления группы: {updateResult.Error}";
                return false;
            }

            var updatedChat = updateResult.Data;

            var currentMembersResult = await _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.Chat.Members(chatDto.Id));
            var currentMemberIds = currentMembersResult.Data?.Select(m => m.Id).ToHashSet() ?? [];

            foreach (var userId in memberIds.Where(id => !currentMemberIds.Contains(id)))
            {
                await _apiClient.PostAsync(ApiEndpoints.Chat.Members(chatDto.Id), new { userId });
            }

            foreach (var userId in currentMemberIds.Where(id => !memberIds.Contains(id) && id != UserId))
            {
                await _apiClient.DeleteAsync(ApiEndpoints.Chat.RemoveMember(chatDto.Id, userId));
            }

            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDTO>(ApiEndpoints.Chat.Avatar(chatDto.Id),avatarStream,
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

    public void SetActiveMenu(int index)
    {
        SelectedMenuIndex = index;
        SetItemCommand.Execute(index);
    }
}
