using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Platform;
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
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatViewModel : BaseViewModel, IAsyncDisposable
    {
        private readonly IApiClientService _apiClient;
        private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;
        private readonly int _chatId;
        private readonly INotificationService _notificationService;
        private HubConnection? _hubConnection;
        private CancellationTokenSource? _loadingCts;
        private bool _isLoadingMessages;
        private int _currentPage = 1;
        private bool _hasMoreMessages = true;
        private bool _disposed;
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

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<MessageViewModel> searchResults = [];

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        private int searchResultsCount;

        public ObservableCollection<LocalFileAttachment> LocalAttachments { get; } = [];

        public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

        private readonly IAuthService _authService;

        public ChatViewModel(
            int chatId,
            ChatsViewModel parent,
            IApiClientService apiClient,
            IAuthService authService,
            IChatInfoPanelStateStore chatInfoPanelStateStore,
            INotificationService notificationService)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _chatInfoPanelStateStore = chatInfoPanelStateStore ?? throw new ArgumentNullException(nameof(chatInfoPanelStateStore));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _chatId = chatId;
            UserId = _authService.UserId ?? throw new InvalidOperationException("User not authenticated");

            _ = InitializeChatAsync();
        }

        /// <summary>
        /// Фабричный метод для создания с полной инициализацией
        /// </summary>
        public static async Task<ChatViewModel> CreateAsync(int chatId,ChatsViewModel parent,IApiClientService apiClient,AuthService authService,IChatInfoPanelStateStore chatInfoPanelStateStore, INotificationService notificationService)
        {
            var vm = new ChatViewModel(chatId, parent, apiClient, authService, chatInfoPanelStateStore, notificationService); 
            await vm.WaitForInitializationAsync();
            return vm;
        }

        private readonly TaskCompletionSource _initTcs = new();

        public Task WaitForInitializationAsync() => _initTcs.Task;

        private async Task InitializeChatAsync()
        {
            _loadingCts = new CancellationTokenSource();
            var ct = _loadingCts.Token;

            try
            {
                IsInitialLoading = true;

                await LoadChatAsync(ct);
                await LoadMembersAsync(ct);
                await LoadInitialMessagesAsync(ct);
                await InitHubAsync(ct);

                _initTcs.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                _initTcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";
                _initTcs.TrySetException(ex);
            }
            finally
            {
                IsInitialLoading = false;
            }
        }

        private async Task LoadChatAsync(CancellationToken ct)
        {
            var result = await _apiClient.GetAsync<ChatDTO>($"api/chats/{_chatId}", ct);

            if (result.Success && result.Data != null)
            {
                if (!string.IsNullOrEmpty(result.Data.Avatar))
                    result.Data.Avatar = GetAvatarUrlWithHash(result.Data.Avatar);

                Chat = result.Data;
            }
            else
            {
                throw new HttpRequestException($"Ошибка загрузки чата: {result.Error}");
            }
        }

        private async Task LoadMembersAsync(CancellationToken ct)
        {
            var result = await _apiClient.GetAsync<List<UserDTO>>($"api/chats/{_chatId}/members", ct);

            if (result.Success && result.Data != null)
            {
                Members = new ObservableCollection<UserDTO>(result.Data);
                return;
            }

            if (Chat != null && !Chat.IsGroup && int.TryParse(Chat.Name, out var otherUserId))
            {
                var members = new ObservableCollection<UserDTO>();

                var meResult = await _apiClient.GetAsync<UserDTO>($"api/user/{UserId}", ct);
                if (meResult.Success && meResult.Data != null)
                    members.Add(meResult.Data);

                var otherResult = await _apiClient.GetAsync<UserDTO>($"api/user/{otherUserId}", ct);
                if (otherResult.Success && otherResult.Data != null && otherResult.Data.Id != UserId)
                    members.Add(otherResult.Data);

                Members = members;
            }
        }

        private async Task LoadInitialMessagesAsync(CancellationToken ct)
        {
            var url = $"api/messages/chat/{_chatId}?userId={UserId}&page=1&pageSize={PageSize}&includeFiles=false";
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result.Success && result.Data != null)
            {
                if (Members.Count == 0 && result.Data.Messages.Count > 0)
                    await LoadMembersAsync(ct);

                SetMessageRelations(result.Data.Messages);

                var vms = result.Data.Messages
                    .Select(msg => new MessageViewModel(msg))
                    .ToList();

                ProcessMessagesMetadata(result.Data.Messages, vms);

                Messages = new ObservableCollection<MessageViewModel>(vms);
                IsScrolledToBottom = true;

                _hasMoreMessages = result.Data.HasMoreMessages;
                _currentPage = result.Data.CurrentPage;
            }
        }

        [RelayCommand]
        private async Task LoadOlderMessages()
        {
            if (!_hasMoreMessages || _isLoadingMessages) return;

            var ct = _loadingCts?.Token ?? CancellationToken.None;

            try
            {
                _isLoadingMessages = true;
                IsLoadingOlderMessages = true;

                var result = await _apiClient.GetAsync<PagedMessagesDTO>(
                    $"api/messages/chat/{_chatId}?userId={UserId}&page={_currentPage + 1}&pageSize={PageSize}", ct);

                if (result.Success && result.Data != null && result.Data.Messages.Count != 0)
                {
                    _hasMoreMessages = result.Data.HasMoreMessages;
                    _currentPage = result.Data.CurrentPage;

                    SetMessageRelations(result.Data.Messages);

                    var vms = result.Data.Messages
                        .Select(msg => new MessageViewModel(msg))
                        .ToList();

                    ProcessMessagesMetadata(result.Data.Messages, vms);

                    foreach (var vm in vms)
                        Messages.Insert(0, vm);

                    UpdatePollsCount();
                }
            }
            finally
            {
                _isLoadingMessages = false;
                IsLoadingOlderMessages = false;
            }
        }

        private async Task InitHubAsync(CancellationToken ct)
        {
            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{App.ApiUrl}chatHub", options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_authService.Token);
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<MessageDTO>("ReceiveMessageDTO", HandleNewMessage);

                await _hubConnection.StartAsync(ct);
                await _hubConnection.InvokeAsync("JoinChat", _chatId.ToString(), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hub connection error: {ex.Message}");
            }
        }

        private void HandleNewMessage(MessageDTO messageDto)
        {
            if (messageDto.ChatId != _chatId) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Messages.Any(m => m.Id == messageDto.Id)) return;

                messageDto.IsOwn = messageDto.SenderId == UserId;

                if (Messages.Count > 0)
                {
                    var lastMessageVm = Messages.Last();
                    messageDto.PreviousMessage = lastMessageVm.Message;
                    messageDto.IsPrevSameSender = lastMessageVm.SenderId == messageDto.SenderId;
                }

                var vm = new MessageViewModel(messageDto);
                ProcessMessagesMetadata([messageDto], [vm]);

                Messages.Add(vm);
                UpdatePollsCount();

                if (!IsScrolledToBottom)
                    HasNewMessages = true;
            });
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessage) && LocalAttachments.Count == 0) return;

            await SafeExecuteAsync(async ct =>
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
                        var uploadResult = await _apiClient.UploadFileAsync<MessageFileDTO>(
                            $"api/files/upload?chatId={_chatId}",
                            local.Data,
                            local.FileName,
                            local.ContentType,
                            ct);

                        if (uploadResult.Success && uploadResult.Data != null)
                            msg.Files.Add(uploadResult.Data);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Upload error: {ex.Message}");
                    }
                }

                var result = await _apiClient.PostAsync<MessageDTO, MessageDTO>("api/messages", msg, ct);

                if (result.Success && result.Data != null)
                {
                    NewMessage = string.Empty;
                    ClearLocalAttachments();
                }
                else
                {
                    ErrorMessage = $"Ошибка отправки: {result.Error}";
                }
            });
        }

        [RelayCommand]
        private void RemoveAttachment(LocalFileAttachment attachment)
        {
            if (LocalAttachments.Remove(attachment))
            {
                attachment.Dispose();
            }
        }

        private void ClearLocalAttachments()
        {
            foreach (var attachment in LocalAttachments)
                attachment.Dispose();

            LocalAttachments.Clear();
        }

        [RelayCommand]
        private void InsertEmoji(string emoji)
        {
            NewMessage += emoji;
        }

        [RelayCommand]
        private async Task OpenCreatePoll()
        {
            if (Parent?.Parent is MainMenuViewModel menu)
            {
                await menu.ShowPollDialogAsync(_chatId, async () => await LoadInitialMessagesAsync(CancellationToken.None));
                return;
            }

            await _notificationService.ShowErrorAsync("Не удалось открыть диалог опроса", copyToClipboard: false);
        }

        [RelayCommand]
        private void ScrollToLatest()
        {
            HasNewMessages = false;
            IsScrolledToBottom = true;
        }

        [RelayCommand]
        private async Task LeaveChat()
        {
            await SafeExecuteAsync(async ct =>
            {
                var result = await _apiClient.PostAsync($"api/chats/{_chatId}/leave?userId={UserId}", null, ct);

                if (result.Success)
                    SuccessMessage = "Вы покинули чат";
                else
                    ErrorMessage = $"Ошибка при выходе из чата: {result.Error}";
            });
        }

        [RelayCommand]
        private async Task SearchMessages()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchResults.Clear();
                SearchResultsCount = 0;
                return;
            }

            await SafeExecuteAsync(async ct =>
            {
                IsSearching = true;

                var result = await _apiClient.GetAsync<SearchMessagesResponseDTO>(
                    $"api/messages/chat/{_chatId}/search?query={Uri.EscapeDataString(SearchQuery)}&userId={UserId}&page=1&pageSize=20", ct);

                if (result.Success && result.Data != null)
                {
                    SearchResultsCount = result.Data.TotalCount;

                    var vms = result.Data.Messages
                        .Select(msg => new MessageViewModel(msg))
                        .ToList();

                    ProcessMessagesMetadata(result.Data.Messages, vms);
                    SearchResults = new ObservableCollection<MessageViewModel>(vms);
                }
                else if (!result.Success)
                {
                    ErrorMessage = $"Ошибка поиска: {result.Error}";
                }
            }, finallyAction: () => IsSearching = false);
        }

        [RelayCommand]
        private void ToggleInfoPanel()
        {
            _chatInfoPanelStateStore.IsOpen = !_chatInfoPanelStateStore.IsOpen;
            OnPropertyChanged(nameof(IsInfoPanelOpen));
        }

        public bool IsInfoPanelOpen
        {
            get => _chatInfoPanelStateStore.IsOpen;
            set
            {
                if (_chatInfoPanelStateStore.IsOpen == value) return;
                _chatInfoPanelStateStore.IsOpen = value;
                OnPropertyChanged(nameof(IsInfoPanelOpen));
            }
        }

        [RelayCommand]
        private async Task AttachFile()
        {
            try
            {
                var platformService = App.Current.Services.GetRequiredService<IPlatformService>();
                var mainWindow = platformService.MainWindow;

                if (mainWindow is null)
                {
                    ErrorMessage = "Не удалось получить главное окно";
                    return;
                }

                var storageProvider = mainWindow.StorageProvider;
                if (storageProvider is null)
                {
                    ErrorMessage = "StorageProvider недоступен";
                    return;
                }

                var options = new FilePickerOpenOptions
                {
                    Title = "Выберите файлы для прикрепления",
                    AllowMultiple = true
                };

                var files = await storageProvider.OpenFilePickerAsync(options);

                foreach (var file in files)
                {
                    var path = file.TryGetLocalPath();
                    if (path is not null)
                    {
                        await AddFileAttachmentAsync(path);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка при выборе файлов: {ex.Message}";
            }
        }


        private async Task AddFileAttachmentAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 20 * 1024 * 1024)
                {
                    System.Diagnostics.Debug.WriteLine($"File too large: {fileInfo.Name}");
                    return;
                }

                var fileName = Path.GetFileName(filePath);
                var contentType = GetMimeType(filePath);

                await using var fileStream = File.OpenRead(filePath);
                var memoryStream = new MemoryStream();
                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var attachment = new LocalFileAttachment
                {
                    FileName = fileName,
                    ContentType = contentType,
                    FilePath = filePath,
                    Data = memoryStream
                };

                if (contentType.StartsWith("image/"))
                {
                    try
                    {
                        memoryStream.Position = 0;
                        attachment.Thumbnail = new Bitmap(memoryStream);
                        memoryStream.Position = 0;
                    }
                    catch
                    {

                    }
                }

                LocalAttachments.Add(attachment);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing file {filePath}: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task OpenProfile(int userId)
        {
            if (Parent?.Parent is MainMenuViewModel menu)
            {
                await menu.ShowUserProfileAsync(userId);
                return;
            }

            await _notificationService.ShowErrorAsync("Не удалось открыть профиль", copyToClipboard: false);
        }

        #region Helper Methods

        private void ProcessMessagesMetadata(List<MessageDTO> messages, List<MessageViewModel>? viewModels = null)
        {
            var userDict = Members.ToDictionary(u => u.Id);

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var vm = viewModels?[i];

                if (!string.IsNullOrEmpty(msg.SenderName) && msg.SenderName != $"User #{msg.SenderId}")
                {
                    if (vm != null) vm.SenderName = msg.SenderName;
                }
                else if (userDict.TryGetValue(msg.SenderId, out var user))
                {
                    var senderName = user.DisplayName ?? user.Username;
                    msg.SenderName = senderName;
                    if (vm != null) vm.SenderName = senderName;
                }
                else
                {
                    var placeholderName = $"User #{msg.SenderId}";
                    msg.SenderName = placeholderName;
                    if (vm != null) vm.SenderName = placeholderName;
                }

                if (string.IsNullOrEmpty(msg.SenderAvatarUrl))
                {
                    if (userDict.TryGetValue(msg.SenderId, out var userForAvatar) && !string.IsNullOrEmpty(userForAvatar.Avatar))
                    {
                        var avatarUrl = GetSafeAvatarUrl(userForAvatar.Avatar);
                        msg.SenderAvatarUrl = avatarUrl;
                        if (vm != null) vm.SenderAvatarUrl = avatarUrl;
                    }
                }
                else
                {
                    if (vm != null) vm.SenderAvatarUrl = msg.SenderAvatarUrl;
                }
            }
        }

        private void SetMessageRelations(List<MessageDTO> messages)
        {
            MessageDTO? previousMessage = null;
            int? lastSenderId = null;

            foreach (var msg in messages)
            {
                msg.IsOwn = msg.SenderId == UserId;
                msg.IsPrevSameSender = lastSenderId != null && msg.SenderId == lastSenderId;
                msg.PreviousMessage = previousMessage;
                msg.ShowSenderName = !msg.IsPrevSameSender;

                lastSenderId = msg.SenderId;
                previousMessage = msg;
            }
        }

        private void UpdatePollsCount() =>
            PollsCount = Messages.Count(m => m.Poll != null);

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

        private static string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".pdf" => "application/pdf",
                ".doc" or ".docx" => "application/msword",
                ".xls" or ".xlsx" => "application/vnd.ms-excel",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }

        partial void OnChatChanged(ChatDTO? value) => OnPropertyChanged(nameof(IsInfoPanelOpen));

        #endregion

        #region Properties

        public List<string> PopularEmojis { get; } =
        [
            "😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅", "😭", "😤", "❤", "👍", "👎", "🎉", "🔥", "✨",
            "💯", "🙏", "👏", "🤝", "💪", "🎁", "📱", "💻", "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"
        ];

        #endregion

        #region IAsyncDisposable

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _loadingCts?.Cancel();
            _loadingCts?.Dispose();
            _loadingCts = null;
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Error disposing hub connection: {ex.Message}"
                    );
                }

                _hubConnection = null;
            }
            ClearLocalAttachments();
            Dispose();
            GC.SuppressFinalize(this);
        }


        #endregion
    }
}