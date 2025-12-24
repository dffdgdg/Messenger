using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public class ChatMessageManager(int chatId,int userId,IApiClientService apiClient,
    Func<ObservableCollection<UserDTO>> getMembersFunc,IFileDownloadService? downloadService = null,INotificationService? notificationService = null)
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<ObservableCollection<UserDTO>> _getMembersFunc = getMembersFunc ?? throw new ArgumentNullException(nameof(getMembersFunc));
    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private bool _hasMoreOlder = true;
    private bool _hasMoreNewer;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading { get; private set; }
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;

    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    public void SetReadInfo(ChatReadInfoDTO? info)
    {
        if (info == null) return;

        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;

        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}, unreadCount={info.UnreadCount}");
    }

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            if (FirstUnreadMessageId.HasValue)
            {
                // Вызываем внутреннюю версию без проверки IsLoading
                return await LoadMessagesAroundInternalAsync(FirstUnreadMessageId.Value, ct);
            }

            var url = $"api/messages/chat/{chatId}?userId={userId}&page=1&pageSize=50";
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                Messages.Clear();

                var members = _getMembersFunc();
                foreach (var msg in result.Data.Messages)
                {
                    var vm = CreateMessageViewModel(msg, members);
                    Messages.Add(vm);
                }

                UpdateBounds();
                _hasMoreOlder = result.Data.HasMoreMessages;
                _hasMoreNewer = false;

                return Messages.Count > 0 ? Messages.Count - 1 : null;
            }

            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }



    /// <summary>
    /// Публичный метод для загрузки сообщений вокруг указанного (например, для поиска)
    /// </summary>
    public async Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            return await LoadMessagesAroundInternalAsync(messageId, ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Внутренняя реализация без проверки IsLoading
    /// </summary>
    private async Task<int?> LoadMessagesAroundInternalAsync(int messageId, CancellationToken ct = default)
    {
        var url = $"api/messages/chat/{chatId}/around/{messageId}?userId={userId}&count=50";
        var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

        if (result is { Success: true, Data: not null })
        {
            Messages.Clear();

            var members = _getMembersFunc();
            int? targetIndex = null;

            for (int i = 0; i < result.Data.Messages.Count; i++)
            {
                var msg = result.Data.Messages[i];
                var vm = CreateMessageViewModel(msg, members);
                Messages.Add(vm);

                if (msg.Id == messageId)
                {
                    targetIndex = i;
                }
            }

            UpdateBounds();
            _hasMoreOlder = result.Data.HasMoreMessages;
            _hasMoreNewer = result.Data.HasNewerMessages;

            Debug.WriteLine($"[MessageManager] Loaded {Messages.Count} messages around {messageId}, targetIndex={targetIndex}");

            return targetIndex;
        }

        Debug.WriteLine($"[MessageManager] Failed to load messages around {messageId}");
        return null;
    }

    public async Task LoadOlderMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMoreOlder || !_oldestLoadedMessageId.HasValue) return;
        IsLoading = true;

        try
        {
            var url = $"api/messages/chat/{chatId}/before/{_oldestLoadedMessageId}?userId={userId}&count=30";
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                var members = _getMembersFunc();

                for (int i = result.Data.Messages.Count - 1; i >= 0; i--)
                {
                    var msg = result.Data.Messages[i];
                    var vm = CreateMessageViewModel(msg, members);
                    Messages.Insert(0, vm);
                }

                UpdateBounds();
                _hasMoreOlder = result.Data.HasMoreMessages;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadNewerMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMoreNewer || !_newestLoadedMessageId.HasValue) return;
        IsLoading = true;

        try
        {
            var url = $"api/messages/chat/{chatId}/after/{_newestLoadedMessageId}?userId={userId}&count=30";
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                var members = _getMembersFunc();

                foreach (var msg in result.Data.Messages)
                {
                    var vm = CreateMessageViewModel(msg, members);
                    Messages.Add(vm);
                }

                UpdateBounds();
                _hasMoreNewer = result.Data.HasNewerMessages;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void AddReceivedMessage(MessageDTO message)
    {
        if (Messages.Any(m => m.Id == message.Id))
            return;

        var members = _getMembersFunc();
        var vm = CreateMessageViewModel(message, members);

        if (message.SenderId != userId)
        {
            vm.IsUnread = true;
        }

        Messages.Add(vm);
        UpdateBounds();
        _hasMoreNewer = false;
    }

    public void MarkAsReadLocally(int messageId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
        {
            msg.IsUnread = false;
        }

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
        {
            LastReadMessageId = messageId;
        }
    }

    public IEnumerable<MessageViewModel> GetUnreadMessages()
    {
        return Messages.Where(m => m.IsUnread && m.SenderId != userId);
    }

    public int GetPollsCount() => Messages.Count(m => m.Poll != null);

    private MessageViewModel CreateMessageViewModel(MessageDTO msg, ObservableCollection<UserDTO> members)
    {
        var sender = members.FirstOrDefault(m => m.Id == msg.SenderId);
        var vm = new MessageViewModel(msg, downloadService, notificationService)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl
        };

        if (LastReadMessageId.HasValue && msg.Id > LastReadMessageId.Value && msg.SenderId != userId)
        {
            vm.IsUnread = true;
        }

        return vm;
    }

    private void UpdateBounds()
    {
        if (Messages.Count > 0)
        {
            _oldestLoadedMessageId = Messages.Min(m => m.Id);
            _newestLoadedMessageId = Messages.Max(m => m.Id);
        }
    }
}