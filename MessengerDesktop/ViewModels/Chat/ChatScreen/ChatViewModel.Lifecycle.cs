using MessengerDesktop.Infrastructure;
using MessengerDesktop.Services.Audio;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatViewModel
{
    /// <summary>
    /// Флаг: были ли подписаны события чата.
    /// Нужен для гарантированной отписки при ошибке инициализации.
    /// </summary>
    private bool _chatEventsSubscribed;

    /// <summary>
    /// Флаг: были ли подписаны события info panel.
    /// </summary>
    private readonly bool _infoPanelEventsSubscribed;

    private async Task InitializeChatAsync()
    {
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        try
        {
            IsInitialLoading = true;

            await LoadChatAsync(ct);
            await LoadMembersAsync(ct);

            SubscribeChatEvents();

            var readInfo = await _globalHub.GetReadInfoAsync(_chatId);
            _messageManager.SetReadInfo(readInfo);

            var scrollToIndex =
                await _messageManager.LoadInitialMessagesAsync(ct);

            await LoadNotificationSettingsAsync(ct);

            UpdatePollsCount();

            var audioRecorder = App.Current.Services
                .GetRequiredService<IAudioRecorderService>();
            InitializeVoice(audioRecorder);

            SubscribeInfoPanelEvents();

            _initTcs.TrySetResult();

            await Task.Delay(150, ct);

            if (scrollToIndex < Messages.Count - 1)
            {
                ScrollToIndexRequested?.Invoke(scrollToIndex.Value, false);
            }
            else
            {
                ScrollToBottomRequested?.Invoke();
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

            // Очистка подписок при ошибке инициализации,
            // чтобы не удерживать VM через делегаты на singleton
            CleanupSubscriptions();

            _initTcs.TrySetException(ex);
        }
        finally
        {
            IsInitialLoading = false;
        }
    }

    private void SubscribeChatEvents()
    {
        _globalHub.MessageReceivedGlobally += OnMessageReceivedForChat;
        _globalHub.MessageUpdatedGlobally += OnMessageUpdatedForChat;
        _globalHub.MessageDeletedGlobally += OnMessageDeletedForChat;
        _globalHub.MessageRead += OnMessageReadForChat;
        _globalHub.UserTyping += OnUserTypingForChat;
        _globalHub.UnreadCountChanged += OnUnreadCountChangedForChat;
        _globalHub.Reconnected += OnHubReconnected;

        _chatEventsSubscribed = true;
    }

    private void UnsubscribeChatEvents()
    {
        if (!_chatEventsSubscribed) return;

        _globalHub.MessageReceivedGlobally -= OnMessageReceivedForChat;
        _globalHub.MessageUpdatedGlobally -= OnMessageUpdatedForChat;
        _globalHub.MessageDeletedGlobally -= OnMessageDeletedForChat;
        _globalHub.MessageRead -= OnMessageReadForChat;
        _globalHub.UserTyping -= OnUserTypingForChat;
        _globalHub.UnreadCountChanged -= OnUnreadCountChangedForChat;
        _globalHub.Reconnected -= OnHubReconnected;

        _chatEventsSubscribed = false;
    }

    /// <summary>
    /// Очистка всех подписок — вызывается и при ошибке init,
    /// и при нормальном dispose.
    /// </summary>
    private void CleanupSubscriptions()
    {
        UnsubscribeChatEvents();
        UnsubscribeInfoPanelEvents();

        if (Members != null)
            Members.CollectionChanged -= OnMembersCollectionChanged;
    }

    #region Chat event filters

    private void OnMessageReceivedForChat(MessageDto message)
    {
        if (_disposed || message.ChatId != _chatId) return;
        OnMessageReceived(message);
    }

    private void OnMessageUpdatedForChat(MessageDto message)
    {
        if (_disposed || message.ChatId != _chatId) return;
        OnMessageUpdatedInChat(message);
    }

    private void OnMessageDeletedForChat(int messageId, int chatId)
    {
        if (_disposed || chatId != _chatId) return;
        OnMessageDeletedInChat(messageId);
    }

    private void OnMessageReadForChat(
        int chatId, int userId, int? lastReadMessageId, DateTime? readAt)
    {
        if (_disposed || chatId != _chatId) return;
        OnMessageRead(lastReadMessageId);
    }

    private void OnUserTypingForChat(int chatId, int userId)
    {
        if (_disposed || chatId != _chatId) return;
        OnUserTyping(userId);
    }

    private void OnUnreadCountChangedForChat(int chatId, int unreadCount)
    {
        if (_disposed || chatId != _chatId) return;
        UnreadCount = unreadCount;
    }

    #endregion

    private void OnHubReconnected()
    {
        if (_disposed) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var ct = _loadingCts?.Token ?? CancellationToken.None;
                var gapFillTask = _messageManager.GapFillAfterReconnectAsync(ct);
                var infoPanelTask = RefreshInfoPanelDataAsync(ct);

                await Task.WhenAll(gapFillTask, infoPanelTask);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatViewModel] Reconnect refresh error: {ex.Message}");
            }
        });
    }

    private async Task LoadChatAsync(CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(_chatId), ct);

        if (result.Success && result.Data is not null)
        {
            if (!string.IsNullOrEmpty(result.Data.Avatar))
            {
                result.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(result.Data.Avatar);
            }

            Chat = result.Data;
        }
        else
        {
            throw new HttpRequestException(
                $"Ошибка загрузки чата: {result.Error}");
        }
    }

    private async Task LoadMembersAsync(CancellationToken ct)
    {
        Members = await _memberLoader.LoadMembersAsync(Chat, ct);

        if (IsContactChat)
            LoadContactUser();

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
            ContactLastSeen = FormatLastSeen(contact);

            if (Chat != null)
            {
                Chat.Name = contact.DisplayName
                    ?? contact.Username ?? Chat.Name;

                if (!string.IsNullOrEmpty(contact.Avatar))
                    Chat.Avatar = contact.Avatar;
            }

            InvalidateAllInfoPanelProperties();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadContactUser error: {ex.Message}");
        }
    }

    private static string? FormatLastSeen(UserDto contact)
    {
        if (contact.IsOnline || !contact.LastOnline.HasValue)
            return null;

        var elapsed = DateTimeOffset.UtcNow - contact.LastOnline.Value;

        return elapsed.TotalMinutes switch
        {
            < 1 => "был(а) только что",
            < 60 => $"был(а) {(int)elapsed.TotalMinutes} мин. назад",
            < 1440 => $"был(а) {(int)elapsed.TotalHours} ч. назад",
            < 2880 => "был(а) вчера",
            < 10080 => $"был(а) {(int)elapsed.TotalDays} дн. назад",
            _ => $"был(а) {contact.LastOnline.Value:dd.MM.yyyy}"
        };
    }

    private async Task LoadNotificationSettingsAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _notificationApiService.GetChatSettingsAsync(_chatId, ct);
            if (settings != null)
                IsNotificationEnabled = settings.NotificationsEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadNotificationSettings error: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupSubscriptions();

        _globalHub.SetCurrentChat(null);

        if (_loadingCts is not null)
        {
            await _loadingCts.CancelAsync();
            _loadingCts.Dispose();
            _loadingCts = null;
        }

        if (_typingIndicatorCts is not null)
        {
            await _typingIndicatorCts.CancelAsync();
            _typingIndicatorCts.Dispose();
            _typingIndicatorCts = null;
        }
        _typingUsers.Clear();

        await DisposeVoiceAsync();

        _attachmentManager.Dispose();
    }
}