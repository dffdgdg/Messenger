using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels.Factories;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class MainMenuViewModel : BaseViewModel
{
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IApiClientService _apiClient;
    private readonly IAuthService _authService;
    private readonly IChatsViewModelFactory _chatsViewModelFactory;
    private readonly IServiceProvider _serviceProvider;

    private ChatsViewModel? _chatsViewModel;
    private ChatsViewModel? _contactsViewModel;
    private ProfileViewModel? _profileViewModel;
    private AdminViewModel? _adminViewModel;
    private SettingsViewModel? _settingsViewModel;

    private CancellationTokenSource? _searchCts;
    private const int SearchDebounceMs = 300;

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
    private ObservableCollection<SearchResultItem> _searchResults = [];

    [ObservableProperty]
    private int _selectedMenuIndex = 1;

    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchText && !HasSearchResults && !IsSearching;
    public bool ShowResults => HasSearchResults;

    public MainMenuViewModel(
        MainWindowViewModel mainWindowViewModel,
        IApiClientService apiClient,
        IAuthService authService,
        IChatsViewModelFactory chatsViewModelFactory,
        IServiceProvider serviceProvider)
    {
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _chatsViewModelFactory = chatsViewModelFactory ?? throw new ArgumentNullException(nameof(chatsViewModelFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        UserId = authService.UserId ?? throw new InvalidOperationException("User not authenticated");

        _chatsViewModel = _chatsViewModelFactory.Create(this, isGroupMode: true);
        CurrentMenuViewModel = _chatsViewModel;

        _ = LoadContactsAndChatsAsync();
    }


    [RelayCommand]
    private void SetItem(int index)
    {
        SelectedMenuIndex = index;
        ClearSearch();

        CurrentMenuViewModel = index switch
        {
            0 => _settingsViewModel ??= new SettingsViewModel(this, _apiClient),
            1 => _chatsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: true),
            3 => _profileViewModel ??= _serviceProvider.GetRequiredService<ProfileViewModel>(),
            4 => _adminViewModel ??= new AdminViewModel(_apiClient, _mainWindowViewModel),
            5 => _contactsViewModel ??= _chatsViewModelFactory.Create(this, isGroupMode: false),
            _ => CurrentMenuViewModel
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        _ = PerformSearchAsync(value, _searchCts.Token);

        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnSearchResultsChanged(ObservableCollection<SearchResultItem> value)
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(ShowResults));
    }

    partial void OnIsSearchingChanged(bool value)
        => OnPropertyChanged(nameof(ShowNoResults));

    private async Task PerformSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchResults = [];
                return;
            }

            IsSearching = true;
            await Task.Delay(SearchDebounceMs, ct);

            if (ct.IsCancellationRequested) return;

            var results = new List<SearchResultItem>();
            var search = query.Trim().ToLowerInvariant();

            var matchingChats = UserChats.Where(c => c.IsGroup &&
            (c.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).Take(5).Select(c => new SearchResultItem
            {
                Type = SearchResultType.Chat,
                Id = c.Id,
                Title = c.Name ?? "Без названия",
                AvatarUrl = c.Avatar,
                Data = c
            });

            results.AddRange(matchingChats);

            var matchingContacts = AllContacts
                .Where(u => (u.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Username?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).Take(5).Select(u => new SearchResultItem
                {
                    Type = SearchResultType.Contact,
                    Id = u.Id,
                    Title = u.DisplayName ?? u.Username ?? "Пользователь",
                    Subtitle = $"@{u.Username}",
                    AvatarUrl = u.Avatar,
                    Data = u,
                    HasExistingChat = UserChats.Any(c => !c.IsGroup && c.Name == u.Id.ToString())
                });

            results.AddRange(matchingContacts);
            SearchResults = new ObservableCollection<SearchResultItem>(results);
        }
        catch (TaskCanceledException)
        {

        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults = [];
    }

    [RelayCommand]
    private async Task SelectSearchResultAsync(SearchResultItem? item)
    {
        if (item == null) return;

        await SafeExecuteAsync(async () =>
        {
            ClearSearch();

            if (item.IsChat && item.Data is ChatDTO chat)
            {
                await OpenChatAsync(chat);
            }
            else if (item.IsContact && item.Data is UserDTO user)
            {
                await OpenOrCreateChatWithUserAsync(user);
            }
        });
    }

    [RelayCommand]
    public async Task OpenOrCreateChatAsync(UserDTO user)
        => await OpenOrCreateChatWithUserAsync(user);

    private async Task OpenOrCreateChatWithUserAsync(UserDTO user)
    {
        await SafeExecuteAsync(async () =>
        {
            var existingChat = UserChats.FirstOrDefault(c => !c.IsGroup && c.Name == user.Id.ToString());

            if (existingChat != null)
            {
                await OpenChatAsync(existingChat);
                return;
            }

            var result = await _apiClient.PostAsync<ChatDTO, ChatDTO>("api/chats", new ChatDTO
            {
                Name = user.Id.ToString(),
                IsGroup = false,
                CreatedById = UserId
            });

            if (result.Success && result.Data != null)
            {
                UserChats.Add(result.Data);
                await OpenChatAsync(result.Data);
            }
            else
            {
                ErrorMessage = $"Ошибка создания чата: {result.Error}";
            }
        });
    }

    private async Task OpenChatAsync(ChatDTO chat)
    {
        _chatsViewModel ??= _chatsViewModelFactory.Create(this, true);

        if (!_chatsViewModel.Chats.Any(c => c.Id == chat.Id))
        {
            _chatsViewModel.Chats.Add(chat);
        }

        _chatsViewModel.SelectedChat = chat;
        CurrentMenuViewModel = _chatsViewModel;
        SelectedMenuIndex = 1;
        await Task.CompletedTask;
    }

    public async Task ShowUserProfileAsync(int userId)
    {
        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.GetAsync<UserDTO>($"api/user/{userId}");

            if (result.Success && result.Data != null)
            {
                var dialog = new UserProfileDialogViewModel(result.Data, _apiClient)
                {
                    SendMessageAction = async message => await SendDirectMessageAsync(result.Data.Id, message)
                };

                await _mainWindowViewModel.ShowDialogAsync(dialog);
            }
            else
            {
                ErrorMessage = $"Не удалось загрузить профиль: {result.Error}";
            }
        });
    }

    public async Task ShowPollDialogAsync(int chatId, Action? onPollCreated = null)
    {
        try
        {
            var pollDialog = new PollDialogViewModel(chatId)
            {
                CreateAction = async pollDto =>
                {
                    await CreatePollAsync(pollDto);
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

    private async Task CreatePollAsync(PollDTO pollDto)
    {
        await SafeExecuteAsync(async () =>
        {
            pollDto.CreatedById = UserId;
            pollDto.MessageId = 0;

            var result = await _apiClient.PostAsync<PollDTO, MessageDTO>("api/poll", pollDto);

            if (result.Success)
                SuccessMessage = "Опрос создан";
            else
                ErrorMessage = $"Ошибка создания опроса: {result.Error}";
        });
    }

    private async Task LoadContactsAndChatsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var usersTask = _apiClient.GetAsync<List<UserDTO>>("api/user");
            var chatsTask = _apiClient.GetAsync<List<ChatDTO>>($"api/chats/user/{UserId}");

            await Task.WhenAll(usersTask, chatsTask);

            var usersResult = await usersTask;
            var chatsResult = await chatsTask;

            if (usersResult.Success && usersResult.Data != null)
            {
                AllContacts = new ObservableCollection<UserDTO>(
                    usersResult.Data.Where(u => u.Id != UserId));
            }

            if (chatsResult.Success && chatsResult.Data != null)
            {
                UserChats = new ObservableCollection<ChatDTO>(chatsResult.Data);
            }
        });
    }

    /// <summary>
    /// Показать диалог создания новой группы
    /// </summary>
    public async Task ShowCreateGroupDialogAsync(Action<ChatDTO>? onGroupCreated = null)
    {
        try
        {
            var dialog = new ChatEditDialogViewModel(_apiClient, UserId)
            {
                SaveAction = async (chatDto, memberIds, avatarStream, avatarFileName) =>
                {
                    return await CreateGroupChatAsync(chatDto, memberIds, avatarStream, avatarFileName, onGroupCreated);
                }
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    /// <summary>
    /// Показать диалог редактирования группы
    /// </summary>
    public async Task ShowEditGroupDialogAsync(ChatDTO chat, Action<ChatDTO>? onGroupUpdated = null)
    {
        try
        {
            // Загружаем участников чата
            var membersResult = await _apiClient.GetAsync<List<UserDTO>>($"api/chats/{chat.Id}/members");
            var members = membersResult.Success ? membersResult.Data : null;

            var dialog = new ChatEditDialogViewModel(_apiClient, UserId, chat, members)
            {
                SaveAction = async (chatDto, memberIds, avatarStream, avatarFileName) =>
                {
                    return await UpdateGroupChatAsync(chatDto, memberIds, avatarStream, avatarFileName, onGroupUpdated);
                }
            };

            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия диалога: {ex.Message}";
        }
    }

    private async Task<bool> CreateGroupChatAsync(
        ChatDTO chatDto,
        List<int> memberIds,
        Stream? avatarStream,
        string? avatarFileName,
        Action<ChatDTO>? onSuccess)
    {
        try
        {
            // 1. Создаём чат
            var createResult = await _apiClient.PostAsync<ChatDTO, ChatDTO>("api/chats", chatDto);

            if (!createResult.Success || createResult.Data == null)
            {
                ErrorMessage = $"Ошибка создания группы: {createResult.Error}";
                return false;
            }

            var createdChat = createResult.Data;

            // 2. Добавляем участников
            foreach (var userId in memberIds)
            {
                await _apiClient.PostAsync($"api/chats/{createdChat.Id}/members", new { userId });
            }

            // 3. Загружаем аватар (если выбран)
            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDTO>(
                    $"api/chats/{createdChat.Id}/avatar",
                    avatarStream,
                    avatarFileName,
                    contentType);

                if (avatarResult.Success && avatarResult.Data != null)
                {
                    createdChat.Avatar = avatarResult.Data.AvatarUrl;
                }
            }

            // 4. Обновляем локальный список чатов
            UserChats.Add(createdChat);

            // 5. Открываем созданный чат
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

    private async Task<bool> UpdateGroupChatAsync(
        ChatDTO chatDto,
        List<int> memberIds,
        Stream? avatarStream,
        string? avatarFileName,
        Action<ChatDTO>? onSuccess)
    {
        try
        {
            // 1. Обновляем информацию о чате
            var updateDto = new UpdateChatDTO
            {
                Id = chatDto.Id,
                Name = chatDto.Name,
                IsGroup = true
            };

            var updateResult = await _apiClient.PutAsync<UpdateChatDTO, ChatDTO>(
                $"api/chats/{chatDto.Id}", updateDto);

            if (!updateResult.Success || updateResult.Data == null)
            {
                ErrorMessage = $"Ошибка обновления группы: {updateResult.Error}";
                return false;
            }

            var updatedChat = updateResult.Data;

            // 2. Обновляем список участников
            // Получаем текущих участников
            var currentMembersResult = await _apiClient.GetAsync<List<UserDTO>>($"api/chats/{chatDto.Id}/members");
            var currentMemberIds = currentMembersResult.Data?.Select(m => m.Id).ToHashSet() ?? [];

            // Добавляем новых участников
            foreach (var userId in memberIds.Where(id => !currentMemberIds.Contains(id)))
            {
                await _apiClient.PostAsync($"api/chats/{chatDto.Id}/members", new { userId });
            }

            // Удаляем убранных участников (кроме создателя)
            foreach (var userId in currentMemberIds.Where(id => !memberIds.Contains(id) && id != UserId))
            {
                await _apiClient.DeleteAsync($"api/chats/{chatDto.Id}/members/{userId}");
            }

            // 3. Загружаем новый аватар (если выбран)
            if (avatarStream != null && !string.IsNullOrEmpty(avatarFileName))
            {
                var contentType = GetMimeType(avatarFileName);
                avatarStream.Position = 0;

                var avatarResult = await _apiClient.UploadFileAsync<AvatarResponseDTO>(
                    $"api/chats/{chatDto.Id}/avatar",
                    avatarStream,
                    avatarFileName,
                    contentType);

                if (avatarResult.Success && avatarResult.Data != null)
                {
                    updatedChat.Avatar = avatarResult.Data.AvatarUrl;
                }
            }

            // 4. Обновляем локальный список
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

    private async Task SendDirectMessageAsync(int userId, string message)
    {
        await SafeExecuteAsync(async () =>
        {
            var chatResult = await _apiClient.PostAsync<ChatDTO, ChatDTO>("api/chats", new ChatDTO
            {
                Name = userId.ToString(),
                IsGroup = false,
                CreatedById = UserId
            });

            if (!chatResult.Success || chatResult.Data == null)
            {
                ErrorMessage = $"Ошибка создания чата: {chatResult.Error}";
                return;
            }

            var messageResult = await _apiClient.PostAsync<MessageDTO, MessageDTO>("api/messages", new MessageDTO
            {
                ChatId = chatResult.Data.Id,
                Content = message,
                SenderId = UserId
            });

            if (messageResult.Success)
            {
                await OpenChatAsync(chatResult.Data);
                SuccessMessage = "Сообщение отправлено";
            }
            else
            {
                ErrorMessage = $"Ошибка отправки: {messageResult.Error}";
            }
        });
    }
}

public enum SearchResultType
{
    Chat,
    Contact
}

public class SearchResultItem
{
    public SearchResultType Type { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public object? Data { get; set; }
    public bool HasExistingChat { get; set; }

    public bool IsChat => Type == SearchResultType.Chat;
    public bool IsContact => Type == SearchResultType.Contact;
    public string TypeText => IsChat ? "Чат" : "Контакт";
    public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
}