using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatsViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;
        private readonly int _userId;
        private readonly AuthService _authService;

        public MainMenuViewModel Parent { get; }

        [ObservableProperty]
        private ObservableCollection<ChatDTO> chats = [];

        [ObservableProperty]
        private ChatDTO? selectedChat;

        [ObservableProperty]
        private ChatViewModel? currentChatViewModel;

        [ObservableProperty]
        private UserProfileDialogViewModel? userProfileDialog;

        private ChatViewModel? _subscribedChatVm;
        private readonly bool isGroupMode = false;
        public ChatsViewModel(int userId, MainMenuViewModel parent, bool isGroupMode)
        {
            _userId = userId;
            Parent = parent;
            this.isGroupMode = isGroupMode;

            _httpClient = App.Current.Services.GetRequiredService<HttpClient>();
            _authService = App.Current.Services.GetRequiredService<AuthService>();
                
            _ = LoadChats();
        }

        [RelayCommand]
        private void OpenChat(ChatDTO? chat)
        {
            try
            {
                if (chat != null)
                {
                    SelectedChat = chat;
                    CurrentChatViewModel = new ChatViewModel(chat.Id, _userId, this);
                    
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в OpenChat: {ex.Message}");
                using var _ = NotificationService.ShowError($"Ошибка открытия чата: {ex.Message}");
            }
        }

        partial void OnSelectedChatChanged(ChatDTO? value)
        {
            if (value != null)
            {
                if (CurrentChatViewModel == null || CurrentChatViewModel.Chat?.Id != value.Id)
                {
                    CurrentChatViewModel = new ChatViewModel(value.Id, _userId, this);
                }
            }
        }


        partial void OnCurrentChatViewModelChanged(ChatViewModel? value)
        {
            // unsubscribe previous
            if (_subscribedChatVm != null)
            {
                _subscribedChatVm.PropertyChanged -= SubscribedChatVm_PropertyChanged;
            }

            _subscribedChatVm = value;

            if (_subscribedChatVm != null)
            {
                _subscribedChatVm.PropertyChanged += SubscribedChatVm_PropertyChanged;
            }

            // notify combined visibility changed
            OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
        }

        private void SubscribedChatVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.IsInfoPanelOpen))
            {
                OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
            }
        }

        // Combined property: true when a chat is open AND that chat's info panel is open
        public bool CombinedIsInfoPanelVisible => CurrentChatViewModel != null && CurrentChatViewModel.IsInfoPanelOpen;

        [RelayCommand]
        private async Task LoadChats()
        {
            try
            {

                if (!_authService.IsAuthenticated)
                {
                    await NotificationService.ShowError("Ошибка авторизации");
                    return;
                }

                if (_httpClient.DefaultRequestHeaders.Authorization == null)
                {
                    if (!string.IsNullOrEmpty(_authService.Token))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authService.Token);
                    }
                }

                var url = $"api/chats/user/{_userId}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ObservableCollection<ChatDTO>>();
                    if (result != null)
                    {
                        var filtered = isGroupMode? result.Where(c => c.IsGroup): result.Where(c => !c.IsGroup);

                        Chats = new ObservableCollection<ChatDTO>(filtered.OrderByDescending(c => c.LastMessageDate));
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Исключение при загрузке чатов: {ex.Message}");
            }
        }
    }
}
