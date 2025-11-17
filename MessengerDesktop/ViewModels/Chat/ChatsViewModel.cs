using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatsViewModel : BaseViewModel
    {
        private readonly IApiClientService _apiClient;
        private readonly AuthService _authService;
        private readonly bool isGroupMode = false;

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

        public ChatsViewModel(MainMenuViewModel parent, bool isGroupMode, IApiClientService apiClient, AuthService authService)
        {
            Parent = parent;
            this.isGroupMode = isGroupMode;
            _apiClient = apiClient;
            _authService = authService;

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
                    CurrentChatViewModel = new ChatViewModel(chat.Id, this, _apiClient);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка открытия чата: {ex.Message}";
            }
        }

        partial void OnSelectedChatChanged(ChatDTO? value)
        {
            if (value != null)
                if (CurrentChatViewModel == null || CurrentChatViewModel.Chat?.Id != value.Id)
                    CurrentChatViewModel = new ChatViewModel(value.Id, this, _apiClient);
        }

        partial void OnCurrentChatViewModelChanged(ChatViewModel? value)
        {
            if (_subscribedChatVm != null)
                _subscribedChatVm.PropertyChanged -= SubscribedChatVm_PropertyChanged;

            _subscribedChatVm = value;

            if (_subscribedChatVm != null)
                _subscribedChatVm.PropertyChanged += SubscribedChatVm_PropertyChanged;

            OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
        }

        private void SubscribedChatVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.IsInfoPanelOpen))
                OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
        }

        public bool CombinedIsInfoPanelVisible => CurrentChatViewModel != null && CurrentChatViewModel.IsInfoPanelOpen;

        [RelayCommand]
        private async Task LoadChats()
        {
            await SafeExecuteAsync(async () =>
            {
                if (!_authService.IsAuthenticated || !_authService.UserId.HasValue)
                {
                    ErrorMessage = "Ошибка авторизации";
                    return;
                }

                var userId = _authService.UserId.Value;
                var result = await _apiClient.GetAsync<List<ChatDTO>>($"api/chats/user/{userId}");

                if (result.Success && result.Data != null)
                {
                    var filtered = isGroupMode ?
                        result.Data.Where(c => c.IsGroup) :
                        result.Data.Where(c => !c.IsGroup);
                    Chats = new ObservableCollection<ChatDTO>(filtered.OrderByDescending(c => c.LastMessageDate));
                    SuccessMessage = "Чаты загружены";
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки чатов: {result.Error}";
                }
            });
        }
    }
}