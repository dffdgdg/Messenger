using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainMenuViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly HttpClient _httpClient;
        private ChatsViewModel? _chatsViewModel, _chatsViewModel2;
        private ProfileViewModel? _profileViewModel;
        private AdminViewModel? _adminViewModel;

        [ObservableProperty]
        private ViewModelBase? currentMenuViewModel;

        [ObservableProperty]
        private UserProfileDialogViewModel? userProfileDialog;

        [ObservableProperty]
        private bool isPollDialogOpen;

        [ObservableProperty]
        private PollDialogViewModel? pollDialogViewModel;

        [ObservableProperty]
        private int userId;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<UserDTO> allContacts = [];

        [ObservableProperty]
        private ObservableCollection<UserDTO> filteredContacts = [];

        [ObservableProperty]
        private ObservableCollection<ChatDTO> userChats = [];

        [ObservableProperty]
        private bool hasSearchResults;

        private Action? _onPollCreated;

        public MainMenuViewModel(MainWindowViewModel mainWindowViewModel, HttpClient httpClient, int userId)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _httpClient = httpClient;
            UserId = userId;
            _ = LoadContactsAndChats();
        }

        [RelayCommand]
        public async Task SetItem(int index)
        {
            try
            {
                switch (index)
                {
                    case 0:
                        CurrentMenuViewModel = new SettingsViewModel(this);
                        break;
                    case 1:
                        _chatsViewModel ??= new ChatsViewModel(UserId, this, true);
                        CurrentMenuViewModel = _chatsViewModel;
                        break;
                    case 3:
                        _profileViewModel ??= new ProfileViewModel(UserId);
                        CurrentMenuViewModel = _profileViewModel;
                        break;
                    case 4:
                        _adminViewModel ??= new AdminViewModel(_httpClient);
                        CurrentMenuViewModel = _adminViewModel;
                        break;
                    case 5:
                        _chatsViewModel2 ??= new ChatsViewModel(UserId, this, false);
                        CurrentMenuViewModel = _chatsViewModel2;
                        break;
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка навигации: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task OpenOrCreateChat(UserDTO user)
        {
            try
            {
                var existingChat = UserChats.FirstOrDefault(c => !c.IsGroup && c.Name == user.Id.ToString());
                ChatDTO? chatToOpen = existingChat;
                if (existingChat == null)
                {
                    var chat = new ChatDTO { Name = user.Id.ToString(), IsGroup = false, CreatedById = UserId };
                    var response = await _httpClient.PostAsJsonAsync("api/chats", chat);
                    if (response.IsSuccessStatusCode)
                    {
                        var created = await response.Content.ReadFromJsonAsync<ChatDTO>();
                        if (created != null)
                        {
                            UserChats.Add(created);
                            chatToOpen = created;
                        }
                    }
                }
                _chatsViewModel ??= new ChatsViewModel(UserId, this, false);
                if (chatToOpen != null)
                {
                    if (!_chatsViewModel.Chats.Any(c => c.Id == chatToOpen.Id)) 
                        _chatsViewModel.Chats.Add(chatToOpen);
                    _chatsViewModel.SelectedChat = chatToOpen;
                }
                CurrentMenuViewModel = _chatsViewModel;
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка: {ex.Message}");
            }
        }


        public async Task ShowPollDialogAsync(int chatId, Action? onPollCreated = null)
        {
            try
            {
                _onPollCreated = onPollCreated;
                PollDialogViewModel = new PollDialogViewModel(chatId)
                {
                    CloseAction = () =>
                    {
                        IsPollDialogOpen = false;
                        PollDialogViewModel = null;
                    },
                    CreateAction = async pollDto =>
                    {
                        await CreatePoll(pollDto);
                        _onPollCreated?.Invoke();
                    }
                };
                IsPollDialogOpen = true;
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка открытия диалога опроса: {ex.Message}");
            }
        }

        private async Task CreatePoll(PollDTO pollDto)
        {
            try
            {
                pollDto.CreatedById = UserId;
                pollDto.MessageId = 0;
                var response = await _httpClient.PostAsJsonAsync("api/poll", pollDto);
                if (response.IsSuccessStatusCode)
                {
                    await NotificationService.ShowSuccess("Опрос создан");
                    IsPollDialogOpen = false;
                }
                else
                {
                    await NotificationService.ShowError("Ошибка создания опроса");
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка создания опроса: {ex.Message}");
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            UpdateFilteredContacts();
        }

        private async Task LoadContactsAndChats()
        {
            try
            {
                var usersResponse = await _httpClient.GetAsync("api/users");
                if (usersResponse.IsSuccessStatusCode)
                {
                    var users = await usersResponse.Content.ReadFromJsonAsync<ObservableCollection<UserDTO>>();
                    if (users != null)
                    {
                        AllContacts = new ObservableCollection<UserDTO>(users.Where(u => u.Id != UserId));
                    }
                }

                var chatsResponse = await _httpClient.GetAsync($"api/chats/user/{UserId}");
                if (chatsResponse.IsSuccessStatusCode)
                {
                    var chats = await chatsResponse.Content.ReadFromJsonAsync<ObservableCollection<ChatDTO>>();
                    if (chats != null)
                    {
                        UserChats = chats;
                    }
                }

                UpdateFilteredContacts();
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private void UpdateFilteredContacts()
        {
            if (AllContacts == null) return;
            
            var search = SearchText?.ToLowerInvariant() ?? string.Empty;
            var dialogUserIds = UserChats
                .Where(c => !c.IsGroup && c.Name != null)
                .Select(c => int.TryParse(c.Name, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToHashSet();

            var withDialog = AllContacts.Where(u => dialogUserIds.Contains(u.Id));
            var withoutDialog = AllContacts.Where(u => !dialogUserIds.Contains(u.Id));
            
            var filtered = withDialog.Concat(withoutDialog)
                .Where(u => string.IsNullOrWhiteSpace(search) || 
                           (u.DisplayName ?? u.Username ?? string.Empty).Contains(search, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            FilteredContacts = new ObservableCollection<UserDTO>(filtered);
            HasSearchResults = FilteredContacts.Count > 0 && !string.IsNullOrWhiteSpace(SearchText);
        }

        public async Task ShowUserProfile(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/user/{userId}");
                if (response.IsSuccessStatusCode)
                {
                    var user = await response.Content.ReadFromJsonAsync<UserDTO>();
                    if (user != null)
                    {
                        var dialog = new UserProfileDialogViewModel(user)
                        {
                            CloseAction = () => UserProfileDialog = null,
                            SendMessageAction = async (message) =>
                            {
                                await SendDirectMessage(user.Id, message);
                                UserProfileDialog = null;
                            }
                        };
                        UserProfileDialog = dialog;
                    }
                }
                else
                {
                    await NotificationService.ShowError("Не удалось загрузить профиль пользователя");
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка загрузки профиля: {ex.Message}");
            }
        }

        private async Task SendDirectMessage(int userId, string message)
        {
            try
            {
                var chatResponse = await _httpClient.PostAsJsonAsync("api/chats", new ChatDTO
                {
                    Name = userId.ToString(),
                    IsGroup = false,
                    CreatedById = UserId
                });

                if (chatResponse.IsSuccessStatusCode)
                {
                    var chat = await chatResponse.Content.ReadFromJsonAsync<ChatDTO>();
                    if (chat != null)
                    {
                        var messageDto = new MessageDTO
                        {
                            ChatId = chat.Id,
                            Content = message,
                            SenderId = UserId
                        };

                        var response = await _httpClient.PostAsJsonAsync("api/messages", messageDto);
                        if (response.IsSuccessStatusCode)
                        {
                            await SetItem(1);
                        }
                        else
                        {
                           await NotificationService.ShowError("Ошибка отправки сообщения");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка отправки сообщения: {ex.Message}");
            }
        }
    }
}