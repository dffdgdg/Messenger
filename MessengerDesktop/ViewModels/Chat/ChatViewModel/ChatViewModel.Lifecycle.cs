using MessengerDesktop.Helpers;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerShared.DTO;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    private async Task InitializeChatAsync()
    {
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        try
        {
            IsInitialLoading = true;

            await LoadChatAsync(ct);
            await LoadMembersAsync(ct);
            await InitHubAsync(ct);

            var readInfo = await _hubConnection!.GetReadInfoAsync();
            _messageManager.SetReadInfo(readInfo);

            var scrollToIndex = await _messageManager.LoadInitialMessagesAsync(ct);

            await LoadNotificationSettingsAsync(ct);

            UpdatePollsCount();
            _initTcs.TrySetResult();

            if (scrollToIndex.HasValue)
            {
                await Task.Delay(150, ct);
                ScrollToIndexRequested?.Invoke(scrollToIndex.Value);
            }
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] Init error: {ex.Message}");
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
        var result = await _apiClient.GetAsync<ChatDTO>(ApiEndpoints.Chat.ById(_chatId), ct);

        if (result.Success && result.Data is not null)
        {
            if (!string.IsNullOrEmpty(result.Data.Avatar))
                result.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(result.Data.Avatar);

            Chat = result.Data;
        }
        else
        {
            throw new HttpRequestException($"Ошибка загрузки чата: {result.Error}");
        }
    }

    private async Task LoadMembersAsync(CancellationToken ct)
    {
        Members = await _memberLoader.LoadMembersAsync(Chat, ct);

        if (IsContactChat)
        {
            LoadContactUser();
        }

        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    private void LoadContactUser()
    {
        try
        {
            var contact = Members.FirstOrDefault(m => m.Id != UserId);
            if (contact == null) return;

            ContactUser = contact;
            IsContactOnline = contact.IsOnline;

            if (!contact.IsOnline && contact.LastOnline.HasValue)
            {
                ContactLastSeen = (DateTime.Now - contact.LastOnline.Value).TotalMinutes switch
                {
                    < 1 => "был(а) только что",
                    < 60 => $"был(а) {(int)(DateTime.Now - contact.LastOnline.Value).TotalMinutes} мин. назад",
                    < 1440 => $"был(а) {(int)(DateTime.Now - contact.LastOnline.Value).TotalHours} ч. назад",
                    < 2880 => "был(а) вчера",
                    < 10080 => $"был(а) {(int)(DateTime.Now - contact.LastOnline.Value).TotalDays} дн. назад",
                    _ => $"был(а) {contact.LastOnline.Value:dd.MM.yyyy}"
                };
            }

            if (Chat != null)
            {
                Chat.Name = contact.DisplayName ?? contact.Username ?? Chat.Name;
                if (!string.IsNullOrEmpty(contact.Avatar))
                {
                    Chat.Avatar = contact.Avatar;
                }
            }

            OnPropertyChanged(nameof(IsContactChat));
            OnPropertyChanged(nameof(IsGroupChat));
            OnPropertyChanged(nameof(InfoPanelTitle));
            OnPropertyChanged(nameof(InfoPanelSubtitle));
            OnPropertyChanged(nameof(ContactUser));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadContactUser error: {ex.Message}");
        }
    }

    private async Task LoadNotificationSettingsAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _notificationApiService.GetChatSettingsAsync(_chatId, ct);
            if (settings != null)
            {
                IsNotificationEnabled = settings.NotificationsEnabled;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadNotificationSettings error: {ex.Message}");
        }
    }

    private async Task InitHubAsync(CancellationToken ct)
    {
        _hubConnection = new ChatHubConnection(_chatId, _authManager);
        _hubConnection.MessageReceived += OnMessageReceived;
        _hubConnection.MessageRead += OnMessageRead;
        await _hubConnection.ConnectAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_globalHub is GlobalHubConnection hub)
        {
            hub.SetCurrentChat(null);
        }

        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;

        if (_hubConnection is not null)
        {
            _hubConnection.MessageReceived -= OnMessageReceived;
            _hubConnection.MessageRead -= OnMessageRead;
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        Messages.Clear();
        Members.Clear();
        LocalAttachments.Clear();
        _attachmentManager.Dispose();

        Dispose();
        GC.SuppressFinalize(this);
    }
}