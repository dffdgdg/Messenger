using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerShared.Dto.Message;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    private void OnMessageReceived(MessageDto messageDto) =>
        Dispatcher.UIThread.Post(async () =>
        {
            _messageManager.AddReceivedMessage(messageDto);
            UpdatePollsCount();

            var addedMsg = Messages.LastOrDefault(m => m.Id == messageDto.Id);
            if (addedMsg != null)
                StartTranscriptionPollingIfNeeded(addedMsg);

            if (!IsScrolledToBottom)
                HasNewMessages = true;
            else
                await MarkMessagesAsReadAsync();
        });

    private void OnMessageUpdatedInChat(MessageDto messageDto) => Dispatcher.UIThread.Post(() => _messageManager.HandleMessageUpdated(messageDto));

    private void OnMessageDeletedInChat(int messageId) => Dispatcher.UIThread.Post(() => _messageManager.HandleMessageDeleted(messageId));

    private void OnMessageRead(int chatId, int userId, int? lastReadMessageId, DateTime? readAt) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!lastReadMessageId.HasValue) return;

            foreach (var msg in Messages.Where(m => m.Id <= lastReadMessageId.Value && m.SenderId == UserId))
            {
                msg.IsRead = true;
            }
        });

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (!message.IsUnread || message.SenderId == UserId)
            return;

        message.IsUnread = false;
        _messageManager.MarkAsReadLocally(message.Id);

        if (_hubConnection != null)
            await _hubConnection.MarkMessageAsReadAsync(message.Id);
    }

    public async Task MarkMessagesAsReadAsync(int? messageId = null)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);

        if (_hubConnection is not null)
            await _hubConnection.MarkAsReadAsync(messageId);
    }

    public async Task OnMessagesVisibleAsync() => await MarkMessagesAsReadAsync();

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (_messageManager.IsLoading) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            IsLoadingOlderMessages = true;
            await _messageManager.LoadOlderMessagesAsync(ct);
            UpdatePollsCount();
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (_messageManager.IsLoading || !_messageManager.HasMoreNewer) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;
        await _messageManager.LoadNewerMessagesAsync(ct);
        UpdatePollsCount();
    }

    /// <summary>
    /// Отправка нового сообщения.
    /// При forward — копирует контент оригинала если пользователь не написал свой текст.
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsEditMode)
        {
            await SaveEditMessage();
            return;
        }

        var forwarding = ForwardingMessage;
        var hasForward = forwarding != null;
        var hasText = !string.IsNullOrWhiteSpace(NewMessage);
        var hasAttachments = LocalAttachments.Count > 0;

        if (!hasText && !hasAttachments && !hasForward)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await _attachmentManager.UploadAllAsync(ct);

            // При forward: если пользователь не написал свой текст,
            // копируем контент оригинального сообщения
            var content = NewMessage;
            if (hasForward && string.IsNullOrWhiteSpace(content))
            {
                content = forwarding!.Content;
            }

            var msg = new MessageDto
            {
                ChatId = _chatId,
                Content = content,
                SenderId = UserId,
                Files = files,
                ReplyToMessageId = ReplyingToMessage?.Id,
                ForwardedFromMessageId = forwarding?.Id
            };

            // При forward: если оригинал имел файлы, а пользователь не добавил свои —
            // копируем файлы оригинала
            if (hasForward && files.Count == 0 && forwarding!.Files.Count > 0)
            {
                msg.Files = forwarding.Files;
            }

            var result = await _apiClient.PostAsync<MessageDto, MessageDto>(
                ApiEndpoints.Message.Create, msg, ct);

            if (result.Success)
            {
                NewMessage = string.Empty;
                _attachmentManager.Clear();
                CancelReply();
                CancelForward();
            }
            else
            {
                ErrorMessage = $"Ошибка отправки: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;
        await MarkMessagesAsReadAsync();
    }

    private void UpdatePollsCount() => PollsCount = _messageManager.GetPollsCount();
}