using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels.Factories;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class ChatsViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthService _authService;
    private readonly IChatViewModelFactory _chatViewModelFactory;
    private readonly bool _isGroupMode;

    public UserProfileDialogViewModel? UserProfileDialog
    {
        get => CurrentChatViewModel?.UserProfileDialog;
        set
        {
            if (CurrentChatViewModel != null)
                CurrentChatViewModel.UserProfileDialog = value;
        }
    }

    public MainMenuViewModel Parent { get; }

    [ObservableProperty]
    private ObservableCollection<ChatDTO> chats = [];

    [ObservableProperty]
    private ChatDTO? selectedChat;

    [ObservableProperty]
    private ChatViewModel? currentChatViewModel;

    private ChatViewModel? _subscribedChatVm;

    public ChatsViewModel(
        MainMenuViewModel parent,
        bool isGroupMode,
        IApiClientService apiClient,
        IAuthService authService,
        IChatViewModelFactory chatViewModelFactory)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _isGroupMode = isGroupMode;
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _chatViewModelFactory = chatViewModelFactory ?? throw new ArgumentNullException(nameof(chatViewModelFactory));

        _ = LoadChats();
    }

    [RelayCommand]
    private void OpenChat(ChatDTO? chat)
    {
        if (chat == null) return;

        try
        {
            SelectedChat = chat;
            CurrentChatViewModel = _chatViewModelFactory.Create(chat.Id, this);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия чата: {ex.Message}";
        }
    }

    partial void OnSelectedChatChanged(ChatDTO? value)
    {
        if (value != null && (CurrentChatViewModel == null || CurrentChatViewModel.Chat?.Id != value.Id))
        {
            CurrentChatViewModel = _chatViewModelFactory.Create(value.Id, this);
        }
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
    [RelayCommand]
    private async Task CreateGroup()
    {
        try
        {
            await Parent.ShowCreateGroupDialogAsync(createdChat =>
            {
                if (!Chats.Any(c => c.Id == createdChat.Id))
                {
                    Chats.Insert(0, createdChat);
                }
                SelectedChat = createdChat;
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка создания группы: {ex.Message}";
        }
    }
    private void SubscribedChatVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsInfoPanelOpen))
            OnPropertyChanged(nameof(CombinedIsInfoPanelVisible));
    }

    public bool CombinedIsInfoPanelVisible =>
        CurrentChatViewModel != null && CurrentChatViewModel.IsInfoPanelOpen;

    [RelayCommand]
    public async Task LoadChats()
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
                var filtered = _isGroupMode
                    ? result.Data.Where(c => c.IsGroup)
                    : result.Data.Where(c => !c.IsGroup);

                Chats = new ObservableCollection<ChatDTO>(
                    filtered.OrderByDescending(c => c.LastMessageDate));
            }
            else
            {
                ErrorMessage = $"Ошибка загрузки чатов: {result.Error}";
            }
        });
    }
}