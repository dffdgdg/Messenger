using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels.Chat;
using MessengerShared.DTO;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatViewModel : BaseViewModel
    {
        private readonly IApiClientService _apiClient;
        private readonly AuthService _authService;
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
        private ObservableCollection<MessageViewModel> messages = [];

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
        private UserProfileDialogViewModel? userProfileDialog;

        public ObservableCollection<MessengerDesktop.ViewModels.Chat.LocalFileAttachment> LocalAttachments { get; } = [];

        public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

        public ChatViewModel(int chatId, ChatsViewModel parent, IApiClientService apiClient)
        {
            _apiClient = apiClient;
            Parent = parent;
            _authService = App.Current.Services.GetRequiredService<AuthService>();
            _chatId = chatId;
            UserId = _authService.UserId ?? throw new InvalidOperationException("User not authenticated");

            _ = InitializeChat();
        }

        private async Task InitializeChat()
        {
            await SafeExecuteAsync(async () =>
            {
                IsInitialLoading = true;
                await LoadChat();
                await LoadMembers();
                await LoadInitialMessages();
                await InitHub();
            }, finallyAction: () => IsInitialLoading = false);
        }


        private async Task LoadChat()
        {
            try
            {
                var result = await _apiClient.GetAsync<ChatDTO>($"api/chats/{_chatId}");
                if (result.Success && result.Data != null)
                {
                    if (!string.IsNullOrEmpty(result.Data.Avatar))
                        result.Data.Avatar = GetAvatarUrlWithHash(result.Data.Avatar);

                    Chat = result.Data;
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки чата: {result.Error}";
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP ошибка: {ex.Message}");
                throw;
            }
        }

        private async Task LoadInitialMessages()
        {
            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] LoadInitialMessages called. Current Members count: {Members.Count}");

                var url = $"api/messages/chat/{_chatId}?userId={UserId}&page=1&pageSize={PageSize}";
                var result = await _apiClient.GetAsync<PagedMessagesDTO>(url);

                if (result.Success && result.Data != null)
                {
                    if (Members.Count == 0 && result.Data.Messages.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Members are empty but messages exist! Reloading members...");
                        await LoadMembers();
                    }

                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] After LoadMembers: Members count = {Members.Count}");

                    SetMessageRelations(result.Data.Messages);

                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] ProcessMessagesMetadata complete. Now loading avatars...");

                    var vmTasks = result.Data.Messages.Select(async msg =>
                    {
                        var vm = new MessageViewModel(msg);
                        if (!string.IsNullOrEmpty(msg.SenderAvatarUrl))
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Loading avatar for message {msg.Id}: {msg.SenderAvatarUrl}");
                                vm.AvatarBitmap = await LoadBitmapAsync(msg.SenderAvatarUrl);
                                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Avatar loaded for message {msg.Id}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Failed to load avatar for message {msg.Id}: {ex.Message}");
                            }
                        }
                        return vm;
                    }).ToList();

                    var vms = await Task.WhenAll(vmTasks);
                    
                    ProcessMessagesMetadata(result.Data.Messages, [.. vms]);
                    
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Loaded {vms.Length} messages with avatars. Final members count: {Members.Count}");
                    Messages = new ObservableCollection<MessageViewModel>(vms);
                }
            });
        }

        private static async Task<Bitmap?> LoadBitmapAsync(string url)
        {
            try
            {
                using var client = new HttpClient();
                await using var stream = await client.GetStreamAsync(url);

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                return new Bitmap(ms);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadBitmapAsync error: {ex.Message}");
                return null;
            }
        }


        private async Task LoadMembers()
        {
            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] LoadMembers called for chatId: {_chatId}");

                var result = await _apiClient.GetAsync<List<UserDTO>>($"api/chats/{_chatId}/members");
                
                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] LoadMembers API result - Success: {result.Success}, Data count: {result.Data?.Count ?? 0}");
                
                if (result.Success && result.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Members loaded from API: {string.Join(", ", result.Data.Select(u => $"{u.DisplayName ?? u.Username} ({u.Id})"))}");
                    Members = new ObservableCollection<UserDTO>(result.Data);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] API members endpoint failed or returned null. Fallback for private chat...");

                if (Chat != null && !Chat.IsGroup)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Chat is private, attempting to parse otherUserId from: {Chat.Name}");
                    
                    if (int.TryParse(Chat.Name, out var otherUserId))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Fetching user data for otherUserId: {otherUserId} and current userId: {UserId}");

                        var otherResult = await _apiClient.GetAsync<UserDTO>($"api/user/{otherUserId}");
                        var meResult = await _apiClient.GetAsync<UserDTO>($"api/user/{UserId}");

                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] otherResult - Success: {otherResult.Success}, User: {otherResult.Data?.DisplayName ?? otherResult.Data?.Username}");
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] meResult - Success: {meResult.Success}, User: {meResult.Data?.DisplayName ?? meResult.Data?.Username}");

                        var members = new ObservableCollection<UserDTO>();
                        if (meResult.Success && meResult.Data != null)
                            members.Add(meResult.Data);
                        if (otherResult.Success && otherResult.Data != null &&
                            (meResult.Data == null || otherResult.Data.Id != meResult.Data.Id))
                            members.Add(otherResult.Data);

                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Fallback members loaded: {string.Join(", ", members.Select(u => $"{u.DisplayName ?? u.Username} ({u.Id})"))}");
                        Members = members;
                    }
                    else
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Failed to parse Chat.Name as userId: {Chat.Name}");
                }
                else
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Chat is null or is group chat. Members remain empty.");
            });
        }

        [RelayCommand]
        private async Task LoadOlderMessages()
        {
            if (!_hasMoreMessages || _isLoadingMessages) return;

            await SafeExecuteAsync(async () =>
            {
                _isLoadingMessages = true;
                IsLoadingOlderMessages = true;

                var result = await _apiClient.GetAsync<PagedMessagesDTO>(
                    $"api/messages/chat/{_chatId}?userId={UserId}&page={_currentPage + 1}&pageSize={PageSize}");

                if (result.Success && result.Data != null && result.Data.Messages.Count != 0)
                {
                    if (Members.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] LoadOlderMessages: Members are empty! Reloading...");
                        await LoadMembers();
                    }

                    _hasMoreMessages = result.Data.HasMoreMessages;
                    _currentPage = result.Data.CurrentPage;

                    var oldMessages = result.Data.Messages;
                    SetMessageRelations(oldMessages);

                    var vmTasks = oldMessages.Select(async msg =>
                    {
                        var vm = new MessageViewModel(msg);
                        if (!string.IsNullOrEmpty(msg.SenderAvatarUrl))
                        {
                            try
                            {
                                vm.AvatarBitmap = await LoadBitmapAsync(msg.SenderAvatarUrl);
                            }
                            catch { }
                        }
                        return vm;
                    }).ToList();

                    var vms = await Task.WhenAll(vmTasks);
                    
                    ProcessMessagesMetadata(oldMessages, [.. vms]);

                    foreach (var vm in vms)
                        Messages.Insert(0, vm);

                    UpdatePollsCount();
                }
                else if (!result.Success)
                {
                    ErrorMessage = $"Ошибка загрузки сообщений: {result.Error}";
                }
            }, finallyAction: () =>
            {
                _isLoadingMessages = false;
                IsLoadingOlderMessages = false;
            });
        }

        private void ProcessMessagesMetadata(List<MessageDTO> messages, List<MessageViewModel>? viewModels = null)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatViewModel] ProcessMessagesMetadata called with {messages.Count} messages");
            System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Available members: {Members.Count} - {string.Join(", ", Members.Select(u => $"{u.DisplayName ?? u.Username} ({u.Id})"))}");

            var userDict = Members.ToDictionary(u => u.Id);

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var vm = viewModels?[i]; 

                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id} from {msg.SenderId}: API SenderName='{msg.SenderName}', API SenderAvatarUrl='{msg.SenderAvatarUrl}'");

                if (!string.IsNullOrEmpty(msg.SenderName) && msg.SenderName != $"User #{msg.SenderId}")
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: Using API SenderName '{msg.SenderName}'");
                    if (vm != null) vm.SenderName = msg.SenderName;
                }
                else if (userDict.TryGetValue(msg.SenderId, out var user))
                {
                    var senderName = user.DisplayName ?? user.Username;
                    msg.SenderName = senderName;
                    if (vm != null) vm.SenderName = senderName;
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: Using Members SenderName '{senderName}'");
                }
                else
                {
                    var placeholderName = $"User #{msg.SenderId}";
                    msg.SenderName = placeholderName;
                    if (vm != null) vm.SenderName = placeholderName;
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: Using placeholder '{placeholderName}'");
                }

                if (string.IsNullOrEmpty(msg.SenderAvatarUrl))
                {
                    if (userDict.TryGetValue(msg.SenderId, out var userForAvatar) && !string.IsNullOrEmpty(userForAvatar.Avatar))
                    {
                        var avatarUrl = GetSafeAvatarUrl(userForAvatar.Avatar);
                        msg.SenderAvatarUrl = avatarUrl;
                        if (vm != null) vm.SenderAvatarUrl = avatarUrl;
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: Using Members Avatar '{avatarUrl}'");
                    }
                    else
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: No avatar found");
                }
                else
                {
                    if (vm != null) vm.SenderAvatarUrl = msg.SenderAvatarUrl;
                    System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Message {msg.Id}: Using API Avatar '{msg.SenderAvatarUrl}'");
                }
            }
        }

        private static string GetSafeAvatarUrl(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
                return "avares://MessengerDesktop/Assets/Images/default-avatar.webp";

            try
            {
                if (avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return avatarUrl;

                var baseUri = new Uri(App.ApiUrl);
                var avatarUri = new Uri(baseUri, avatarUrl.TrimStart('/'));
                return avatarUri.ToString();
            }
            catch
            {
                return "avares://MessengerDesktop/Assets/Images/default-avatar.webp";
            }
        }

        private void UpdatePollsCount() =>
            PollsCount = Messages.Count(m => m.Poll != null);

        private void SetMessageRelations(List<MessageDTO> messages)
        {
            MessageDTO? previousMessage = null;
            int? lastSenderId = null;

            foreach (var msg in messages)
            {
                msg.IsOwn = (msg.SenderId == UserId);
                msg.IsPrevSameSender = lastSenderId != null && msg.SenderId == lastSenderId;
                msg.PreviousMessage = previousMessage;
                msg.ShowSenderName = !msg.IsPrevSameSender;

                lastSenderId = msg.SenderId;
                previousMessage = msg;
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessage) && LocalAttachments.Count == 0) return;

            await SafeExecuteAsync(async () =>
            {
                var msg = new MessageDTO
                {
                    ChatId = _chatId,
                    Content = NewMessage,
                    SenderId = UserId,
                    Files = []
                };

                foreach (var local in LocalAttachments.ToList())
                {
                    try
                    {
                        local.Data.Position = 0;
                        var uploadResult = await _apiClient.UploadFileAsync<MessengerShared.DTO.MessageFileDTO>($"api/files/upload?chatId={_chatId}", local.Data, local.FileName, local.ContentType);
                        if (uploadResult.Success && uploadResult.Data != null)
                            msg.Files.Add(uploadResult.Data);
                        else
                            System.Diagnostics.Debug.WriteLine($"File upload failed: {uploadResult.Error}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Upload attachment error: {ex.Message}");
                    }
                }

                var result = await _apiClient.PostAsync<MessageDTO, MessageDTO>("api/messages", msg);

                if (result.Success && result.Data != null)
                {
                    NewMessage = string.Empty;
                    LocalAttachments.Clear();
                }
                else ErrorMessage = $"Ошибка отправки: {result.Error}";
            });
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
                
                _hubConnection = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub", options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(authService.Token);
                    }).WithAutomaticReconnect().Build();

                _hubConnection.On<MessageDTO>("ReceiveMessageDTO", HandleNewMessage);

                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("JoinChat", _chatId.ToString());
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка подключения к чату: {ex.Message}";
            }
        }

        private async void HandleNewMessage(MessageDTO messageDto)
        {
            if (messageDto.ChatId != _chatId) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (Messages.Any(m => m.Id == messageDto.Id)) return;

                messageDto.IsOwn = messageDto.SenderId == UserId;

                if (Messages.Count > 0)
                {
                    var lastMessageVm = Messages.Last();
                    messageDto.PreviousMessage = lastMessageVm.Message; // <-- берем DTO
                    messageDto.IsPrevSameSender = lastMessageVm.SenderId == messageDto.SenderId;
                }

                var vm = new MessageViewModel(messageDto);

                if (!string.IsNullOrEmpty(messageDto.SenderAvatarUrl))
                {
                    try { vm.AvatarBitmap = await LoadBitmapAsync(messageDto.SenderAvatarUrl); }
                    catch { }
                }

                ProcessMessagesMetadata([messageDto], [vm]);

                Messages.Add(vm);
                UpdatePollsCount();

                if (!IsScrolledToBottom)
                    HasNewMessages = true;
            });
        }

        [RelayCommand]
        private async Task LeaveChat()
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PostAsync($"api/chats/{_chatId}/leave?userId={UserId}", null);

                if (result.Success)
                    SuccessMessage = "Вы покинули чат";
                else
                    ErrorMessage = $"Ошибка при выходе из чата: {result.Error}";
            });
        }

        [RelayCommand]
        public async Task OpenProfile(int userId)
        {
            if (Parent?.Parent is MainMenuViewModel menu)
            {
                await menu.ShowUserProfileAsync(userId);
                return;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel mwvm)
            {
                await mwvm.ShowDialogAsync(
                    new UserProfileDialogViewModel(
                        new UserDTO { Id = userId }, 
                        App.Current.Services.GetRequiredService<IApiClientService>())
                );
                return;
            }

            await NotificationService.ShowError("Не удалось открыть профиль", copyToClipboard: false);
        }

        private static string GetAvatarUrlWithHash(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
                return string.Empty;

            if (avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var uriBuilder = new UriBuilder(avatarUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                query["v"] = DateTime.Now.Ticks.ToString();
                uriBuilder.Query = query.ToString();
                return uriBuilder.ToString();
            }

            var baseUrl = App.ApiUrl.TrimEnd('/');
            var cleanAvatarPath = avatarUrl.TrimStart('/');
            return $"{baseUrl}/{cleanAvatarPath}?v={DateTime.Now.Ticks}";
        }

        [RelayCommand]
        private void ToggleInfoPanel()
        {
            var current = ChatInfoPanelStateStore.Get();
            ChatInfoPanelStateStore.Set(!current);
            OnPropertyChanged(nameof(IsInfoPanelOpen));
        }

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

        partial void OnChatChanged(ChatDTO? value) => OnPropertyChanged(nameof(IsInfoPanelOpen));
    }
}
