using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainMenuViewModel : BaseViewModel
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly IApiClientService _apiClient;
        private readonly AuthService _authService;
        private ChatsViewModel? _chatsViewModel, _chatsViewModel2;
        private ProfileViewModel? _profileViewModel;
        private AdminViewModel? _adminViewModel;

        [ObservableProperty]
        private BaseViewModel? _currentMenuViewModel;

        [ObservableProperty]
        private int _userId;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<UserDTO> _allContacts = [];

        [ObservableProperty]
        private ObservableCollection<UserDTO> _filteredContacts = [];

        [ObservableProperty]
        private ObservableCollection<ChatDTO> _userChats = [];

        [ObservableProperty]
        private bool _hasSearchResults;

        public MainMenuViewModel(MainWindowViewModel mainWindowViewModel,
                           IApiClientService apiClient,
                           AuthService authService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _apiClient = apiClient;
            _authService = authService;
            UserId = authService.UserId ?? throw new InvalidOperationException("User not authenticated");

            _ = LoadContactsAndChats();
        }

        [RelayCommand]
        public async Task SetItem(int index)
        {
            await SafeExecuteAsync(async () =>
            {
                switch (index)
                {
                    case 0:
                        CurrentMenuViewModel = new SettingsViewModel(this, _apiClient);
                        break;
                    case 1:
                        _chatsViewModel ??= new ChatsViewModel(this, true, _apiClient, _authService);
                        CurrentMenuViewModel = _chatsViewModel;
                        break;
                    case 3:
                        _profileViewModel ??= new ProfileViewModel(UserId, _apiClient);
                        CurrentMenuViewModel = _profileViewModel;
                        break;
                    case 4:
                        _adminViewModel ??= new AdminViewModel(_apiClient, _mainWindowViewModel);
                        CurrentMenuViewModel = _adminViewModel;
                        break;
                    case 5:
                        _chatsViewModel2 ??= new ChatsViewModel(this, false, _apiClient, _authService);
                        CurrentMenuViewModel = _chatsViewModel2;
                        break;
                }
            }, "Переключение раздела");
        }

        [RelayCommand]
        public async Task OpenOrCreateChat(UserDTO user)
        {
            await SafeExecuteAsync(async () =>
            {
                var existingChat = UserChats.FirstOrDefault(c => !c.IsGroup && c.Name == user.Id.ToString());
                ChatDTO? chatToOpen = existingChat;

                if (existingChat == null)
                {
                    var chat = new ChatDTO { Name = user.Id.ToString(), IsGroup = false, CreatedById = UserId };

                    var result = await _apiClient.PostAsync<ChatDTO, ChatDTO>("api/chats", chat);

                    if (result.Success && result.Data != null)
                    {
                        UserChats.Add(result.Data);
                        chatToOpen = result.Data;
                        System.Diagnostics.Debug.WriteLine($"New chat created: {result.Data.Id}");
                    }
                    else
                    {
                        ErrorMessage = $"Ошибка создания чата: {result.Error}";
                        return;
                    }
                }

                _chatsViewModel ??= new ChatsViewModel(this, false, _apiClient, _authService);
                if (chatToOpen != null)
                {
                    if (!_chatsViewModel.Chats.Any(c => c.Id == chatToOpen.Id))
                        _chatsViewModel.Chats.Add(chatToOpen);
                    _chatsViewModel.SelectedChat = chatToOpen;
                }
                CurrentMenuViewModel = _chatsViewModel;
                SuccessMessage = "Чат открыт";
            });
        }

        public async Task ShowUserProfileAsync(int userId)
        {
            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] ShowUserProfileAsync called for userId: {userId}");

                var result = await _apiClient.GetAsync<UserDTO>($"api/user/{userId}");

                if (result.Success && result.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] User loaded: {result.Data.DisplayName ?? result.Data.Username}");

                    var dialog = new UserProfileDialogViewModel(result.Data, _apiClient)
                    {
                        SendMessageAction = async (message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] SendMessageAction callback invoked");
                            await SendDirectMessage(result.Data.Id, message);
                        }
                    };

                    System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] Calling ShowDialogAsync for UserProfileDialog");
                    await _mainWindowViewModel.ShowDialogAsync(dialog);
                    System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] ShowDialogAsync completed");
                }
                else 
                {
                    ErrorMessage = $"Не удалось получить данные пользователя: {result.Error}";
                    System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] Failed to load user: {result.Error}");
                }
            });
        }

        public async Task ShowPollDialogAsync(int chatId, Action? onPollCreated = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] ShowPollDialogAsync called for chatId: {chatId}");

                var pollDialog = new PollDialogViewModel(chatId)
                {
                    CreateAction = async pollDto => 
                    { 
                        System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] Poll CreateAction callback invoked");
                        await CreatePoll(pollDto);
                        onPollCreated?.Invoke();
                    }
                };

                System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] Calling ShowDialogAsync for PollDialog");
                await _mainWindowViewModel.ShowDialogAsync(pollDialog);
                System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] ShowDialogAsync completed");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка открытия диалога опроса: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MainMenuViewModel] ShowPollDialogAsync error: {ex}");
            }
        }

        private async Task CreatePoll(PollDTO pollDto)
        {
            await SafeExecuteAsync(async () =>
            {
                pollDto.CreatedById = UserId;
                pollDto.MessageId = 0;

                var result = await _apiClient.PostAsync<PollDTO, MessageDTO>("api/poll", pollDto);

                if (result.Success && result.Data != null)
                {
                    SuccessMessage = "Опрос создан";
                }
                else
                {
                    ErrorMessage = $"Ошибка создания опроса: {result.Error}";
                    System.Diagnostics.Debug.WriteLine($"CreatePoll failed: {result.Error}, Details: {result.Details}");
                }
            });
        }

        partial void OnSearchTextChanged(string value) => UpdateFilteredContacts();

        private async Task LoadContactsAndChats()
        {
            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"LoadContactsAndChats: UserId={UserId}");

                var usersResult = await _apiClient.GetAsync<List<UserDTO>>("api/user");
                if (usersResult.Success && usersResult.Data != null)
                {
                    AllContacts = new ObservableCollection<UserDTO>(usersResult.Data.Where(u => u.Id != UserId));
                    System.Diagnostics.Debug.WriteLine($"Users loaded: {AllContacts.Count} contacts");
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки пользователей: {usersResult.Error}";
                    System.Diagnostics.Debug.WriteLine($"Users load failed: {usersResult.Error}");
                    return;
                }

                var chatsResult = await _apiClient.GetAsync<List<ChatDTO>>($"api/chats/user/{UserId}");
                if (chatsResult.Success && chatsResult.Data != null)
                {
                    UserChats = new ObservableCollection<ChatDTO>(chatsResult.Data);
                    System.Diagnostics.Debug.WriteLine($"Chats loaded: {UserChats.Count} chats");
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки чатов: {chatsResult.Error}";
                    System.Diagnostics.Debug.WriteLine($"Chats load failed: {chatsResult.Error}");
                    return;
                }

                UpdateFilteredContacts();
                SuccessMessage = "Данные загружены";
            });
        }

        private void UpdateFilteredContacts()
        {
            if (AllContacts == null) return;

            var search = SearchText?.ToLowerInvariant() ?? string.Empty;
            var dialogUserIds = UserChats.Where(c => !c.IsGroup && c.Name != null).
                Select(c => int.TryParse(c.Name, out var id) ? id : (int?)null).Where(id => id.HasValue).Select(id => id.Value).ToHashSet();

            var withDialog = AllContacts.Where(u => dialogUserIds.Contains(u.Id));
            var withoutDialog = AllContacts.Where(u => !dialogUserIds.Contains(u.Id));

            var filtered = withDialog.Concat(withoutDialog)
                .Where(u => string.IsNullOrWhiteSpace(search) || (u.DisplayName ?? u.Username ?? string.Empty).
                Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();

            FilteredContacts = new ObservableCollection<UserDTO>(filtered);
            HasSearchResults = FilteredContacts.Count > 0 && !string.IsNullOrWhiteSpace(SearchText);
        }

        private async Task SendDirectMessage(int userId, string message)
        {
            await SafeExecuteAsync(async () =>
            {
                var chatResult = await _apiClient.PostAsync<ChatDTO, ChatDTO>("api/chats", new ChatDTO
                {
                    Name = userId.ToString(),
                    IsGroup = false,
                    CreatedById = UserId
                });

                if (chatResult.Success && chatResult.Data != null)
                {
                    var messageResult = await _apiClient.PostAsync<MessageDTO, MessageDTO>("api/messages", new MessageDTO
                    {
                        ChatId = chatResult.Data.Id,
                        Content = message,
                        SenderId = UserId
                    });

                    if (messageResult.Success)
                    {
                        await SetItem(1);
                        SuccessMessage = "Сообщение отправлено";
                    }
                    else ErrorMessage = $"Ошибка отправки сообщения: {messageResult.Error}";
                }
                else ErrorMessage = $"Ошибка создания чата: {chatResult.Error}";
            });
        }
    }
}