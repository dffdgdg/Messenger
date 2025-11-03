using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;
        private readonly int _chatId;
        private HubConnection? _hubConnection;
        private bool _isLoadingMessages;
        private int _currentPage = 1;
        private bool _hasMoreMessages = true;
        private const int PageSize = 50;

        public ChatsViewModel Parent { get; }

        [ObservableProperty]
        private ChatDTO? chat;

        [ObservableProperty]
        private ObservableCollection<UserDTO> members = [];

        [ObservableProperty]
        private ObservableCollection<MessageDTO> messages = [];

        [ObservableProperty]
        private bool isInitialLoading = true;

        [ObservableProperty]
        private bool isLoadingOlderMessages;

        [ObservableProperty]
        private bool hasNewMessages;

        [ObservableProperty]
        private bool isScrolledToBottom = true;

        [ObservableProperty]
        private int pollsCount;

        [ObservableProperty]
        private string newMessage = string.Empty;

        [ObservableProperty]
        private int userId;

        [ObservableProperty]
        private string? errorMessage;

        [ObservableProperty]
        private UserProfileDialogViewModel? userProfileDialog;

        public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

        public ChatViewModel(int chatId, int userId, ChatsViewModel parent)
        {
            Parent = parent;
            _httpClient = App.Current.Services.GetRequiredService<HttpClient>();
            
            _chatId = chatId;
            this.userId = userId;
            Converters.PollToPollViewModelConverter.UserId = userId;

            _ = InitializeChat();
        }

        private async Task InitializeChat()
        {
            IsInitialLoading = true;
            try
            {
                await Task.WhenAll(
                    LoadChat(),
                    LoadMembers(),
                    LoadInitialMessages()
                );
                await InitHub();
            }
            finally
            {
                IsInitialLoading = false;
            }
        }

        private async Task LoadChat()
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<ChatDTO>($"api/chats/{_chatId}");
                if (result != null)
                {
                    // Добавляем хэш к аватару чата
                    if (!string.IsNullOrEmpty(result.Avatar))
                    {
                        result.Avatar = GetAvatarUrlWithHash(result.Avatar);
                    }
                    Chat = result;
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка загрузки чата: {ex.Message}");
            }
        }

        private async Task LoadInitialMessages()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadInitialMessages: ChatId={_chatId}, UserId={UserId}");

                var url = $"api/messages/chat/{_chatId}?userId={UserId}&page=1&pageSize={PageSize}";
                System.Diagnostics.Debug.WriteLine($"Запрос сообщений: {url}");

                // Проверяем заголовки HttpClient
                var httpClient = App.Current.Services.GetRequiredService<HttpClient>();
                System.Diagnostics.Debug.WriteLine($"HttpClient Auth Header: {httpClient.DefaultRequestHeaders.Authorization}");

                var response = await httpClient.GetAsync(url);
                System.Diagnostics.Debug.WriteLine($"Response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Messages response: {json}");

                    var result = await response.Content.ReadFromJsonAsync<PagedMessagesDTO>();

                    if (result != null)
                    {
                        _hasMoreMessages = result.HasMoreMessages;
                        _currentPage = result.CurrentPage;

                        var messages = result.Messages;
                        System.Diagnostics.Debug.WriteLine($"Получено сообщений: {messages.Count}");

                        SetMessageRelations(messages);
                        ProcessMessagesMetadata(messages);
                        Messages = new ObservableCollection<MessageDTO>(messages);
                        UpdatePollsCount();

                        System.Diagnostics.Debug.WriteLine($"Сообщения загружены успешно");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Результат messages = null");
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки сообщений: {response.StatusCode}, {error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Исключение в LoadInitialMessages: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                NotificationService.ShowError($"Ошибка загрузки сообщений: {ex.Message}");
            }
        }

        private async Task LoadMembers()
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<ObservableCollection<UserDTO>>($"api/chats/{_chatId}/members");
                if (result != null && result.Count > 0)
                {
                    Members = result;
                    return;
                }

                if (Chat != null && !Chat.IsGroup)
                {
                    if (int.TryParse(Chat.Name, out var otherUserId))
                    {
                        var other = await _httpClient.GetFromJsonAsync<UserDTO>($"api/user/{otherUserId}");
                        var me = await _httpClient.GetFromJsonAsync<UserDTO>($"api/user/{UserId}");
                        var members = new ObservableCollection<UserDTO>();
                        if (me != null) members.Add(me);
                        if (other != null && (me == null || other.Id != me.Id)) members.Add(other);
                        Members = members;
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка загрузки участников: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task LoadOlderMessages()
        {
            if (!_hasMoreMessages || _isLoadingMessages)
                return;

            try
            {
                _isLoadingMessages = true;
                IsLoadingOlderMessages = true;

                var result = await _httpClient.GetFromJsonAsync<PagedMessagesDTO>(
                    $"api/messages/chat/{_chatId}?userId={userId}&page={_currentPage + 1}&pageSize={PageSize}");

                if (result != null && result.Messages.Count != 0)
                {
                    _hasMoreMessages = result.HasMoreMessages;
                    _currentPage = result.CurrentPage;

                    var oldMessages = result.Messages;
                    SetMessageRelations(oldMessages);
                    ProcessMessagesMetadata(oldMessages);

                    foreach (var message in oldMessages)
                    {
                        Messages.Insert(0, message);
                    }

                    UpdatePollsCount();
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка загрузки старых сообщений: {ex.Message}");
            }
            finally
            {
                _isLoadingMessages = false;
                IsLoadingOlderMessages = false;
            }
        }

        private void ProcessMessagesMetadata(List<MessageDTO> messages)
        {
            System.Diagnostics.Debug.WriteLine($"ProcessMessagesMetadata: обрабатываем {messages.Count} сообщений");

            var userDict = Members.ToDictionary(u => u.Id);
            foreach (var msg in messages)
            {
                if (userDict.TryGetValue(msg.SenderId, out var user))
                {
                    msg.SenderName = user.DisplayName ?? user.Username;

                    System.Diagnostics.Debug.WriteLine($"Сообщение {msg.Id}: SenderId={msg.SenderId}, UserAvatar={user.Avatar}");

                    if (!string.IsNullOrEmpty(user.Avatar))
                    {
                        var avatarUrl = GetAvatarUrlWithHash(user.Avatar);
                        msg.SenderAvatarUrl = avatarUrl;
                        System.Diagnostics.Debug.WriteLine($"Avatar URL: {avatarUrl}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Avatar is null or empty for user {user.Id}");
                        msg.SenderAvatarUrl = null;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"User {msg.SenderId} not found in members dictionary");
                    msg.SenderAvatarUrl = null;
                }
            }
        }

        private void UpdatePollsCount()
        {
            PollsCount = Messages.Count(m => m.Poll != null);
        }

        private void SetMessageRelations(List<MessageDTO> messages)
        {
            MessageDTO? previousMessage = null;
            int? lastSenderId = null;

            foreach (var msg in messages)
            {
                msg.IsOwn = (msg.SenderId == UserId);
                msg.IsPrevSameSender = lastSenderId != null && msg.SenderId == lastSenderId;
                msg.PreviousMessage = previousMessage;
                msg.IsMentioned = (msg.Mentions != null && msg.Mentions.Any(m => m.UserId == UserId)) && !msg.IsOwn;
                msg.ShowSenderName = !msg.IsPrevSameSender;

                lastSenderId = msg.SenderId;
                previousMessage = msg;
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessage))
                return;

            try
            {
                var msg = new MessageDTO
                {
                    ChatId = _chatId,
                    Content = NewMessage,
                    SenderId = UserId,
                };

                var response = await _httpClient.PostAsJsonAsync("api/messages", msg);
                if (response.IsSuccessStatusCode)
                {
                    NewMessage = string.Empty;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    NotificationService.ShowError($"Ошибка отправки: {error}");
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task OpenCreatePoll()
        {
            if (Parent?.Parent is MainMenuViewModel menu)
            {
                await menu.ShowPollDialogAsync(_chatId, async () => await LoadInitialMessages());
                return;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel mwvm &&
                mwvm.CurrentViewModel is MainMenuViewModel mainMenu)
            {
                await mainMenu.ShowPollDialogAsync(_chatId, async () => await LoadInitialMessages());
                return;
            }

            await NotificationService.ShowError("Не удалось открыть диалог опроса", copyToClipboard: false);
        }

        [RelayCommand]
        private void ScrollToLatest()
        {
            HasNewMessages = false;
            IsScrolledToBottom = true;
        }

        private async Task InitHub()
        {
            try
            {
                var authService = App.Current.Services.GetRequiredService<AuthService>();
                if (string.IsNullOrEmpty(authService.Token))
                {
                    throw new InvalidOperationException("Authentication token is missing");
                }

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{App.ApiUrl}chatHub", options =>
                          {
                              options.AccessTokenProvider = () => Task.FromResult(authService.Token);
                          })
                          .WithAutomaticReconnect()
                      .Build();

                _hubConnection.On<MessageDTO>("ReceiveMessageDTO", HandleNewMessage);

                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("JoinChat", _chatId.ToString());
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка подключения к чату: {ex.Message}");
            }
        }

        private void HandleNewMessage(MessageDTO messageDto)
        {
            if (messageDto.ChatId != _chatId) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Messages.Any(m => m.Id == messageDto.Id))
                    return;

                messageDto.IsOwn = messageDto.SenderId == UserId;
                if (Messages.Count > 0)
                {
                    var lastMessage = Messages.Last();
                    messageDto.PreviousMessage = lastMessage;
                    messageDto.IsPrevSameSender = lastMessage != null && messageDto.SenderId == lastMessage.SenderId;
                }
                messageDto.IsMentioned = (messageDto.Mentions != null && messageDto.Mentions.Any(m => m.UserId == UserId)) && !messageDto.IsOwn;

                ProcessMessagesMetadata([messageDto]);
                Messages.Add(messageDto);
                UpdatePollsCount();

                if (!IsScrolledToBottom)
                {
                    HasNewMessages = true;
                }
            });
        }

        [RelayCommand]
        private async Task LeaveChat()
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/chats/{_chatId}/leave?userId={userId}", null);
                if (response.IsSuccessStatusCode)
                {
                    NotificationService.ShowSuccess("Вы покинули чат", copyToClipboard: false);
                }
                else
                {
                    NotificationService.ShowError("Ошибка при выходе из чата", copyToClipboard: false);
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError($"Ошибка: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task OpenProfile(int userId)
        {
            if (Parent?.Parent is MainMenuViewModel menu)
            {
                await menu.ShowUserProfile(userId);
                return;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel mwvm &&
                mwvm.CurrentViewModel is MainMenuViewModel mainMenu)
            {
                await mainMenu.ShowUserProfile(userId);
                return;
            }

            await NotificationService.ShowError("Не удалось открыть профиль", copyToClipboard: false);
        }

        private static string GetAvatarUrlWithHash(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
                return string.Empty;

            if (avatarUrl.StartsWith("http"))
            {
                return $"{avatarUrl}?v={DateTime.Now.Ticks}";
            }

            return $"{App.ApiUrl.TrimEnd('/')}{avatarUrl}?v={DateTime.Now.Ticks}";
        }

        [RelayCommand]
        private void ToggleInfoPanel()
        {
            // Toggle global panel state
            var current = ChatInfoPanelStateStore.Get();
            ChatInfoPanelStateStore.Set(!current);
            OnPropertyChanged(nameof(IsInfoPanelOpen));
        }

        // New property backed by global store to persist panel open state (global, not per-chat)
        public bool IsInfoPanelOpen
        {
            get => ChatInfoPanelStateStore.Get();
            set
            {
                if (ChatInfoPanelStateStore.Get() == value) return;
                ChatInfoPanelStateStore.Set(value);
                OnPropertyChanged(nameof(IsInfoPanelOpen));
            }
        }

        // Called by source generator when Chat property changes
        partial void OnChatChanged(ChatDTO? value)
        {
            // Notify that IsInfoPanelOpen may have changed for the new chat
            OnPropertyChanged(nameof(IsInfoPanelOpen));
        }
    }
}
