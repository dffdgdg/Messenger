using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.ReadReceipt;
using MessengerShared.DTO.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public class ChatMessageManager(
    int chatId,
    int userId,
    IApiClientService apiClient,
    Func<ObservableCollection<UserDTO>> getMembersFunc,
    IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null)
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

        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}");
    }

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            if (FirstUnreadMessageId.HasValue)
            {
                return await LoadMessagesAroundInternalAsync(FirstUnreadMessageId.Value, ct);
            }

            var url = ApiEndpoints.Message.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                Messages.Clear();

                var members = _getMembersFunc();
                foreach (var msg in result.Data.Messages)
                {
                    Messages.Add(CreateMessageViewModel(msg, members));
                }

                UpdateBounds();
                UpdateDateSeparators();
                RecalculateGrouping();
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

    private async Task<int?> LoadMessagesAroundInternalAsync(int messageId, CancellationToken ct = default)
    {
        var url = ApiEndpoints.Message.Around(chatId, messageId, userId, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

        if (result is { Success: true, Data: not null })
        {
            Messages.Clear();

            var members = _getMembersFunc();
            int? targetIndex = null;

            for (int i = 0; i < result.Data.Messages.Count; i++)
            {
                var msg = result.Data.Messages[i];
                Messages.Add(CreateMessageViewModel(msg, members));

                if (msg.Id == messageId)
                {
                    targetIndex = i;
                }
            }

            UpdateBounds();
            UpdateDateSeparators();
            RecalculateGrouping();
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
            var url = ApiEndpoints.Message.Before(chatId, _oldestLoadedMessageId.Value, userId, AppConstants.LoadMorePageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                var members = _getMembersFunc();
                var insertedCount = result.Data.Messages.Count;

                for (int i = result.Data.Messages.Count - 1; i >= 0; i--)
                {
                    Messages.Insert(0, CreateMessageViewModel(result.Data.Messages[i], members));
                }

                UpdateBounds();
                UpdateDateSeparators();

                // Пересчитываем группировку для вставленных + граница со старыми
                // Проще пересчитать всё, т.к. вставка была в начало
                RecalculateGrouping();

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
            var url = ApiEndpoints.Message.After(chatId, _newestLoadedMessageId.Value, userId, AppConstants.LoadMorePageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                var members = _getMembersFunc();
                var startIndex = Messages.Count;

                foreach (var msg in result.Data.Messages)
                {
                    Messages.Add(CreateMessageViewModel(msg, members));
                }

                UpdateBounds();
                UpdateDateSeparators();

                // Пересчёт группировки: от предыдущего последнего до конца
                if (startIndex > 0)
                {
                    // Обновляем стык старых и новых + все новые
                    for (int i = Math.Max(0, startIndex - 1); i < Messages.Count; i++)
                    {
                        UpdateGroupingForIndex(i);
                    }
                }
                else
                {
                    RecalculateGrouping();
                }

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
        UpdateDateSeparatorForNewMessage(vm);

        // Группировка: обновляем последний добавленный + предыдущий
        var index = Messages.Count - 1;
        MessageViewModel.UpdateGroupingAround(Messages, index);

        _hasMoreNewer = false;
    }

    public void HandleMessageDeleted(int messageId)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == messageId);
        if (msg == null) return;

        var index = Messages.IndexOf(msg);
        msg.MarkAsDeleted();

        // После удаления группировка могла измениться:
        // удалённые сообщения разрывают группу
        MessageViewModel.UpdateGroupingAround(Messages, index);
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
        => Messages.Where(m => m.IsUnread && m.SenderId != userId);

    public int GetPollsCount()
        => Messages.Count(m => m.Poll != null);

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

    #region Grouping

    /// <summary>
    /// Полный пересчёт группировки для всех сообщений
    /// </summary>
    private void RecalculateGrouping()
    {
        MessageViewModel.RecalculateGrouping(Messages);
    }

    /// <summary>
    /// Пересчёт группировки для одного индекса
    /// </summary>
    private void UpdateGroupingForIndex(int index)
    {
        if (index < 0 || index >= Messages.Count) return;

        var current = Messages[index];
        var prev = index > 0 ? Messages[index - 1] : null;
        var next = index < Messages.Count - 1 ? Messages[index + 1] : null;

        current.IsContinuation = prev != null && MessageViewModel.CanGroup(prev, current);
        current.HasNextFromSame = next != null && MessageViewModel.CanGroup(current, next);
    }

    #endregion

    #region Bounds & Date Separators

    private void UpdateBounds()
    {
        if (Messages.Count > 0)
        {
            _oldestLoadedMessageId = Messages.Min(m => m.Id);
            _newestLoadedMessageId = Messages.Max(m => m.Id);
        }
    }

    private void UpdateDateSeparators()
    {
        DateTime? previousDate = null;

        foreach (var message in Messages)
        {
            var messageDate = message.CreatedAt.Date;

            if (previousDate == null || messageDate != previousDate.Value)
            {
                message.ShowDateSeparator = true;
                message.DateSeparatorText = FormatDateSeparator(messageDate);
            }
            else
            {
                message.ShowDateSeparator = false;
                message.DateSeparatorText = null;
            }

            previousDate = messageDate;
        }
    }

    private void UpdateDateSeparatorForNewMessage(MessageViewModel newMessage)
    {
        var messageDate = newMessage.CreatedAt.Date;

        var index = Messages.IndexOf(newMessage);
        if (index <= 0)
        {
            newMessage.ShowDateSeparator = true;
            newMessage.DateSeparatorText = FormatDateSeparator(messageDate);
            return;
        }

        var previousMessage = Messages[index - 1];
        var previousDate = previousMessage.CreatedAt.Date;

        if (messageDate != previousDate)
        {
            newMessage.ShowDateSeparator = true;
            newMessage.DateSeparatorText = FormatDateSeparator(messageDate);
        }
        else
        {
            newMessage.ShowDateSeparator = false;
            newMessage.DateSeparatorText = null;
        }
    }

    private static string FormatDateSeparator(DateTime date)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        if (date == today)
            return "Сегодня";

        if (date == yesterday)
            return "Вчера";

        if (date.Year == today.Year)
            return date.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

        return date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    #endregion
}